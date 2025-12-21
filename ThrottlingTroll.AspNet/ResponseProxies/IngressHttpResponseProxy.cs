using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// Abstraction layer on top of <see cref="HttpResponse"/>
    /// </summary>
    public class IngressHttpResponseProxy : IIngressHttpResponseProxy
    {
        internal IngressHttpResponseProxy(HttpResponse response)
        {
            this.Response = response;
        }

        /// <inheritdoc />
        public HttpResponse Response { get; private set; }

        /// <inheritdoc />
        public int StatusCode
        {
            get
            {
                return this.Response.StatusCode;
            }
            set
            {
                this.Response.StatusCode = value;
            }
        }

        /// <inheritdoc />
        public void SetHttpHeader(string headerName, string headerValue)
        {
            this.Response.Headers.Remove(headerName);
            this.Response.Headers.Add(headerName, headerValue);
        }

        /// <inheritdoc />
        public Task WriteAsync(string text)
        {
            return this.Response.WriteAsync(text);
        }

        /// <inheritdoc />
        public bool ShouldContinueAsNormal { get; set; }


        internal async Task ConstructResponse(
            HttpContext context,
            List<LimitCheckResult> checkList,
            IHttpRequestProxy requestProxy, 
            Func<Task> callNextOnce,
            Func<List<LimitCheckResult>, IHttpRequestProxy, IHttpResponseProxy, CancellationToken, Task> responseFabric)
        {
            var exceededLimit = checkList
                .Where(r => r.RequestsRemaining < 0)
                // Sorting by the suggested RetryAfter header value (which is expected to be in seconds) in descending order
                .OrderByDescending(r => r.RetryAfterInSeconds)
                .FirstOrDefault();

            if (exceededLimit == null)
            {
                return;
            }

            // Exceeded rule's ResponseFabric takes precedence.
            // But 1) it can be null and 2) result.Rule can also be null (when 429 is propagated from egress)
            responseFabric = exceededLimit.Rule?.ResponseFabric ?? responseFabric;

            if (responseFabric == null)
            {
                // For Circuit Breaker returning 503 Service Unavailable
                this.StatusCode = exceededLimit.Rule?.LimitMethod is CircuitBreakerRateLimitMethod ?
                    StatusCodes.Status503ServiceUnavailable :
                    StatusCodes.Status429TooManyRequests;

                // Formatting default Retry-After response

                if (!string.IsNullOrEmpty(exceededLimit.RetryAfterHeaderValue))
                {
                    this.SetHttpHeader(HeaderNames.RetryAfter, exceededLimit.RetryAfterHeaderValue);
                }

                string responseString = DateTime.TryParse(exceededLimit.RetryAfterHeaderValue, out var dt) ?
                    exceededLimit.RetryAfterHeaderValue :
                    $"{exceededLimit.RetryAfterHeaderValue} seconds";

                await this.WriteAsync($"Retry after {responseString}");
            }
            else
            {
                // Using the provided response builder

                await responseFabric(checkList, requestProxy, this, context.RequestAborted);

                if (this.ShouldContinueAsNormal)
                {
                    // Continue with normal request processing
                    await callNextOnce();
                }
            }
        }
    }
}