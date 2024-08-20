using Grpc.Core;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("ThrottlingTroll.AzureFunctions.Tests")]

namespace ThrottlingTroll
{
    /// <summary>
    /// Implements ingress throttling for Azure Functions
    /// </summary>
    public class ThrottlingTrollMiddleware : ThrottlingTroll
    {
        internal ThrottlingTrollMiddleware
        (
            ThrottlingTrollOptions options

        ) : base(options.Log, options.CounterStore, options.GetConfigFunc, options.IdentityIdExtractor, options.CostExtractor, options.IntervalToReloadConfigInSeconds)
        {
            this._responseFabric = options.ResponseFabric;
        }

        /// <summary>
        /// Is invoked by Azure Functions middleware pipeline. Handles ingress throttling.
        /// </summary>
        public async Task<HttpResponseData> InvokeAsync(HttpRequestData request, Func<Task> next, CancellationToken cancellationToken)
        {
            var requestProxy = new IncomingHttpRequestProxy(request);
            var cleanupRoutines = new List<Func<Task>>();
            try
            {
                // Need to call the rest of the pipeline no more than one time
                var callNextOnce = ThrottlingTrollCoreExtensions.RunOnce(
                    // Here we could just add a continuation task to the result of this._next(), but then CheckAndBreakTheCircuit() would not get executed, if an exception is thrown at the synchronous part of this._next().
                    // So we'll have to make an async lambda
                    async () =>
                    {
                        try
                        {
                            await next();

                            // Adding/removing internal circuit breaking rules
                            await this.CheckAndBreakTheCircuit(requestProxy, new IngressHttpResponseProxy(request.FunctionContext.GetHttpResponseData()), null);
                        }
                        catch (Exception ex)
                        {
                            // Adding/removing internal circuit breaking rules
                            await this.CheckAndBreakTheCircuit(requestProxy, null, ex);

                            throw;
                        }
                    });

                var checkList = await this.IsIngressOrEgressExceededAsync(requestProxy, cleanupRoutines, callNextOnce);

                return await this.ConstructResponse(request, checkList, requestProxy, callNextOnce, cancellationToken);
            }
            finally
            {
                await Task.WhenAll(cleanupRoutines.Select(f => f()));
            }
        }

        private readonly Func<List<LimitCheckResult>, IHttpRequestProxy, IHttpResponseProxy, CancellationToken, Task> _responseFabric;

        private async Task<HttpResponseData> ConstructResponse(HttpRequestData request, List<LimitCheckResult> checkList, IHttpRequestProxy requestProxy, Func<Task> callNextOnce, CancellationToken cancellationToken)
        {
            var exceededLimit = checkList
                .Where(r => r.RequestsRemaining < 0)
                // Sorting by the suggested RetryAfter header value (which is expected to be in seconds) in descending order
                .OrderByDescending(r => r.RetryAfterInSeconds)
                .FirstOrDefault();

            if (exceededLimit == null)
            {
                return null;
            }

            // Exceeded rule's ResponseFabric takes precedence.
            // But 1) it can be null and 2) result.Rule can also be null (when 429 is propagated from egress)
            var responseFabric = exceededLimit.Rule?.ResponseFabric ?? this._responseFabric;

            var response = request.CreateResponse(HttpStatusCode.OK);

            if (responseFabric == null)
            {
                // For Circuit Breaker returning 503 Service Unavailable
                response.StatusCode = exceededLimit.Rule?.LimitMethod is CircuitBreakerRateLimitMethod ?
                    HttpStatusCode.ServiceUnavailable :
                    HttpStatusCode.TooManyRequests;

                // Formatting default Retry-After response

                if (!string.IsNullOrEmpty(exceededLimit.RetryAfterHeaderValue))
                {
                    response.Headers.Add("Retry-After", exceededLimit.RetryAfterHeaderValue);
                }

                string responseString = DateTime.TryParse(exceededLimit.RetryAfterHeaderValue, out var dt) ?
                    exceededLimit.RetryAfterHeaderValue :
                    $"{exceededLimit.RetryAfterHeaderValue} seconds";

                await response.WriteStringAsync($"Retry after {responseString}");

                return response;
            }
            else
            {
                // Using the provided response builder

                var responseProxy = new IngressHttpResponseProxy(response);

                await responseFabric(checkList, requestProxy, responseProxy, cancellationToken);

                if (responseProxy.ShouldContinueAsNormal)
                {
                    // Continue with normal request processing
                    await callNextOnce();

                    return null;
                }
                else
                {
                    return response;
                }
            }
        }
    }
}
