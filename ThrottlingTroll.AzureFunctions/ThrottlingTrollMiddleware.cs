using Microsoft.Azure.Functions.Worker;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("ThrottlingTroll.AzureFunctions.Tests")]

namespace ThrottlingTroll
{
    /// <summary>
    /// Implements ingress throttling for Azure Functions
    /// </summary>
    public class ThrottlingTrollMiddleware : ThrottlingTrollCore
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
        public async Task<IngressHttpResponseProxyBase> InvokeAsync(
            FunctionContext context, 
            Func<Task> next)
        {
            IIncomingHttpRequestProxy requestProxy;
            IngressHttpResponseProxyBase responseProxy;

            var httpContext = context.GetHttpContext();
            if (httpContext == null)
            {
                // "classic" function
                var requestData = await context.GetHttpRequestDataAsync() ?? throw new ArgumentNullException("HTTP Request is null");
                requestProxy = new IncomingHttpRequestDataProxy(requestData);

                responseProxy = new IngressHttpResponseDataProxy();
            }
            else
            {
                // ASP.Net Core Integration
                requestProxy = new IncomingHttpRequestProxy(context);

                responseProxy = new IngressHttpResponseProxy(httpContext.Response);
            }

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
                            await responseProxy.OnResponseCompleted(
                                () => this.CheckAndBreakTheCircuit(requestProxy, (IHttpResponseProxy)responseProxy, null));
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
                    checkList,
                    requestProxy,
                    callNextOnce,
                    this._responseFabric,
                    context.CancellationToken);

                return responseProxy;
            }
            finally
            {
                await Task.WhenAll(cleanupRoutines.Select(f => f()));
            }
        }

        private readonly Func<List<LimitCheckResult>, IHttpRequestProxy, IHttpResponseProxy, CancellationToken, Task> _responseFabric;
    }
}