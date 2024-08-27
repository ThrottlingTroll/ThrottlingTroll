﻿using Microsoft.AspNetCore.Http;
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
                var callNextOnce = ThrottlingTrollCoreExtensions.RunOnce(
                    // Here we could just add a continuation task to the result of this._next(), but then CheckAndBreakTheCircuit() would not get executed, if an exception is thrown at the synchronous part of this._next().
                    // So we'll have to make an async lambda
                    async () =>
                    {
                        try
                        {
                            await next();

                            // With ASP.Net Core Integration, at this point context.Response is not yet populated.
                            // So we'll have to add the limit check as response's OnComplete() event
                            context.Response.OnCompleted(() => this.CheckAndBreakTheCircuit(requestProxy, new IngressHttpResponseProxy(context.Response), null));
                        }
                        catch (Exception ex)
                        {
                            // Adding/removing internal circuit breaking rules
                            await this.CheckAndBreakTheCircuit(requestProxy, null, ex);

                            throw;
                        }
                    });

                var checkList = await this.IsIngressOrEgressExceededAsync(requestProxy, cleanupRoutines, callNextOnce);

                await this.ConstructResponse(context, checkList, requestProxy, callNextOnce);
            }
            finally
            {
                await Task.WhenAll(cleanupRoutines.Select(f => f()));
            }
        }

        private readonly Func<List<LimitCheckResult>, IHttpRequestProxy, IHttpResponseProxy, CancellationToken, Task> _responseFabric;

        private async Task ConstructResponse(HttpContext context, List<LimitCheckResult> checkList, IHttpRequestProxy requestProxy, Func<Task> callNextOnce)
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
            var responseFabric = exceededLimit.Rule?.ResponseFabric ?? this._responseFabric;

            if (responseFabric == null)
            {
                // For Circuit Breaker returning 503 Service Unavailable
                context.Response.StatusCode = exceededLimit.Rule?.LimitMethod is CircuitBreakerRateLimitMethod ? 
                    StatusCodes.Status503ServiceUnavailable : 
                    StatusCodes.Status429TooManyRequests;

                // Formatting default Retry-After response

                if (!string.IsNullOrEmpty(exceededLimit.RetryAfterHeaderValue))
                {
                    context.Response.Headers.Add(HeaderNames.RetryAfter, exceededLimit.RetryAfterHeaderValue);
                }

                string responseString = DateTime.TryParse(exceededLimit.RetryAfterHeaderValue, out var dt) ?
                    exceededLimit.RetryAfterHeaderValue :
                    $"{exceededLimit.RetryAfterHeaderValue} seconds";

                await context.Response.WriteAsync($"Retry after {responseString}");
            }
            else
            {
                // Using the provided response builder

                var responseProxy = new IngressHttpResponseProxy(context.Response);

                await responseFabric(checkList, requestProxy, responseProxy, context.RequestAborted);

                if (responseProxy.ShouldContinueAsNormal)
                {
                    // Continue with normal request processing
                    await callNextOnce();
                }
            }
        }
    }
}
