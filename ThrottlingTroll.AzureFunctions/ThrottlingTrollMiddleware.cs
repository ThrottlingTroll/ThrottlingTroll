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
        private readonly Func<List<LimitCheckResult>, IHttpRequestProxy, IHttpResponseProxy, CancellationToken, Task> _responseFabric;

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
                var callNextOnce = ThrottlingTrollCoreExtensions.RunOnce(() => next());

                var checkList = await this.IsIngressOrEgressExceededAsync(requestProxy, cleanupRoutines, callNextOnce);

                return await this.ConstructResponse(request, checkList, requestProxy, callNextOnce, cancellationToken);
            }
            finally
            {
                await Task.WhenAll(cleanupRoutines.Select(f => f()));
            }
        }

        private async Task<HttpResponseData> ConstructResponse(HttpRequestData request, List<LimitCheckResult> checkList, IHttpRequestProxy requestProxy, Func<Task> callNextOnce, CancellationToken cancellationToken)
        {
            var result = checkList
                .Where(r => r.RequestsRemaining < 0)
                // Sorting by the suggested RetryAfter header value (which is expected to be in seconds) in descending order
                .OrderByDescending(r => r.RetryAfterInSeconds)
                .FirstOrDefault();

            if (result == null)
            {
                return null;
            }

            var response = request.CreateResponse(HttpStatusCode.OK);

            if (this._responseFabric == null)
            {
                response.StatusCode = HttpStatusCode.TooManyRequests;

                // Formatting default Retry-After response

                if (!string.IsNullOrEmpty(result.RetryAfterHeaderValue))
                {
                    response.Headers.Add("Retry-After", result.RetryAfterHeaderValue);
                }

                string responseString = DateTime.TryParse(result.RetryAfterHeaderValue, out var dt) ?
                    result.RetryAfterHeaderValue :
                    $"{result.RetryAfterHeaderValue} seconds";

                await response.WriteStringAsync($"Retry after {responseString}");

                return response;
            }
            else
            {
                // Using the provided response builder

                var responseProxy = new IngressHttpResponseProxy(response);

                await this._responseFabric(checkList, requestProxy, responseProxy, cancellationToken);

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
