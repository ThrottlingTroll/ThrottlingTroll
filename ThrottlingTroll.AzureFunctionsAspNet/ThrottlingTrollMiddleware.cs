using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Net.Http.Headers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("ThrottlingTroll.AzureFunctionsAspNet.Tests")]

namespace ThrottlingTroll
{
    /// <summary>
    /// Implements ingress throttling for Azure Functions with ASP.NET Core integration.
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
        public async Task Invoke(FunctionContext functionContext, Func<Task> next)
        {
            var context = functionContext.GetHttpContext();
            var requestProxy = new IncomingHttpRequestProxy(functionContext);
            var cleanupRoutines = new List<Func<Task>>();
            try
            {
                // Need to call the rest of the pipeline no more than one time
                var callNextOnce = ThrottlingTrollCoreExtensions.RunOnce(() => next());

                var checkList = await this.IsIngressOrEgressExceededAsync(requestProxy, cleanupRoutines, callNextOnce);

                await this.ConstructResponse(context, checkList, requestProxy, callNextOnce);
            }
            finally
            {
                await Task.WhenAll(cleanupRoutines.Select(f => f()));
            }
        }

        private async Task ConstructResponse(HttpContext context, List<LimitCheckResult> checkList, IHttpRequestProxy requestProxy, Func<Task> callNextOnce)
        {
            var result = checkList
                .Where(r => r.RequestsRemaining < 0)
                // Sorting by the suggested RetryAfter header value (which is expected to be in seconds) in descending order
                .OrderByDescending(r => r.RetryAfterInSeconds)
                .FirstOrDefault();

            if (result == null)
            {
                return;
            }

            if (this._responseFabric == null)
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                // Formatting default Retry-After response

                if (!string.IsNullOrEmpty(result.RetryAfterHeaderValue))
                {
                    context.Response.Headers.Add(HeaderNames.RetryAfter, result.RetryAfterHeaderValue);
                }

                string responseString = DateTime.TryParse(result.RetryAfterHeaderValue, out var dt) ?
                    result.RetryAfterHeaderValue :
                    $"{result.RetryAfterHeaderValue} seconds";

                await context.Response.WriteAsync($"Retry after {responseString}");
            }
            else
            {
                // Using the provided response builder

                var responseProxy = new IngressHttpResponseProxy(context.Response);

                await this._responseFabric(checkList, requestProxy, responseProxy, context.RequestAborted);

                if (responseProxy.ShouldContinueAsNormal)
                {
                    // Continue with normal request processing
                    await callNextOnce();
                }
            }
        }
    }
}
