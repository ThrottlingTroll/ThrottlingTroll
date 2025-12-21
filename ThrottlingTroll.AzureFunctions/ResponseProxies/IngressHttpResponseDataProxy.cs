using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// Abstraction layer on top of <see cref="HttpResponseData"/>
    /// </summary>
    public class IngressHttpResponseDataProxy : IngressHttpResponseProxyBase, IIngressHttpResponseDataProxy
    {
        internal IngressHttpResponseDataProxy()
        {
        }

        /// <inheritdoc />
        public HttpResponseData ResponseData { get; private set; }

        /// <inheritdoc />
        public int StatusCode
        {
            get
            {
                return (int)(this.ResponseData?.StatusCode ?? 0);
            }
            set
            {
                if (this.ResponseData != null)
                {
                    this.ResponseData.StatusCode = (HttpStatusCode)value;
                }
            }
        }

        /// <inheritdoc />
        public void SetHttpHeader(string headerName, string headerValue)
        {
            this.ResponseData?.Headers.Remove(headerName);
            this.ResponseData?.Headers.Add(headerName, headerValue);
        }

        /// <inheritdoc />
        public async Task WriteAsync(string text)
        {
            if (this.ResponseData != null)
            {
                await this.ResponseData.WriteStringAsync(text);
            }
        }

        /// <inheritdoc />
        internal override async Task ConstructResponse(
            List<LimitCheckResult> checkList,
            IHttpRequestProxy requestProxy,
            Func<Task> callNextOnce,
            Func<List<LimitCheckResult>, IHttpRequestProxy, IHttpResponseProxy, CancellationToken, Task> responseFabric,
            CancellationToken cancellationToken)
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

            // Need to initialize ResponseData before we're being passed to responseFabric
            this.ResponseData = ((IIncomingHttpRequestDataProxy)requestProxy)
                .RequestData
                .CreateResponse(HttpStatusCode.OK);

            if (responseFabric == null)
            {
                // For Circuit Breaker returning 503 Service Unavailable
                this.StatusCode = exceededLimit.Rule?.LimitMethod is CircuitBreakerRateLimitMethod ?
                    StatusCodes.Status503ServiceUnavailable :
                    StatusCodes.Status429TooManyRequests;

                // Formatting default Retry-After response
                if (!string.IsNullOrEmpty(exceededLimit.RetryAfterHeaderValue))
                {
                    this.SetHttpHeader("Retry-After", exceededLimit.RetryAfterHeaderValue);
                }

                string responseString = DateTime.TryParse(exceededLimit.RetryAfterHeaderValue, out var dt) ?
                    exceededLimit.RetryAfterHeaderValue :
                    $"{exceededLimit.RetryAfterHeaderValue} seconds";

                await this.WriteAsync($"Retry after {responseString}");
            }
            else
            {
                // Using the provided response fabric
                await responseFabric(checkList, requestProxy, this, cancellationToken);

                if (this.ShouldContinueAsNormal)
                {
                    // Resetting ResponseData, so that it does not get applied
                    this.ResponseData = null;

                    // Continue with normal request processing
                    await callNextOnce();
                }
            }
        }

        /// <inheritdoc />
        internal override void Apply()
        {
            // Need to explicitly set invocation result to response data at the end of request processing
            if (this.ResponseData != null)
            {
                this.ResponseData.FunctionContext.GetInvocationResult().Value = this.ResponseData;
            }
        }

        /// <inheritdoc />
        internal override Task OnResponseCompleted(Func<Task> action) => action();
    }
}