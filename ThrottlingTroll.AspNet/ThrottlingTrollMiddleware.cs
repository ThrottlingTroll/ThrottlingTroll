using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// Implements ingress throttling
    /// </summary>
    public class ThrottlingTrollMiddleware : ThrottlingTrollCore
    {
        /// <summary>
        /// Ctor. Shold not be used externally, but needs to be public.
        /// </summary>
        public ThrottlingTrollMiddleware
        (
            RequestDelegate next,
            ThrottlingTrollOptions options

        ) : base(options.Log, options.CounterStore, options.GetConfigFunc, options.IdentityIdExtractor, options.CostExtractor, options.IntervalToReloadConfigInSeconds)
        {
            this._next = next;
            this._responseFabric = options.ResponseFabric;
        }

        /// <summary>
        /// Is invoked by ASP.NET middleware pipeline. Handles ingress throttling.
        /// </summary>
        public async Task InvokeAsync(HttpContext context)
        {
            var requestProxy = new IncomingHttpRequestProxy(context.Request);
            var responseProxy = new IngressHttpResponseProxy(context.Response);
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
                            await this._next(context);

                            // Adding/removing internal circuit breaking rules
                            await this.CheckAndBreakTheCircuit(requestProxy, responseProxy, null);
                        }
                        catch (Exception ex)
                        {
                            // Adding/removing internal circuit breaking rules
                            await this.CheckAndBreakTheCircuit(requestProxy, null, ex);

                            throw;
                        }
                    });

                var checkList = await this.IsIngressOrEgressExceededAsync(requestProxy, cleanupRoutines, callNextOnce);

                await responseProxy.ConstructResponse(
                    context,
                    checkList,
                    requestProxy,
                    callNextOnce,
                    this._responseFabric);
            }
            finally
            {
                await Task.WhenAll(cleanupRoutines.Select(f => f()));
            }
        }

        private readonly RequestDelegate _next;

        private readonly Func<List<LimitCheckResult>, IHttpRequestProxy, IHttpResponseProxy, CancellationToken, Task> _responseFabric;
    }
}