using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
            var requestProxy = new IncomingHttpRequestProxy(context.Request);
            var cleanupRoutines = new List<Func<Task>>();
            try
            {
                // Need to call the rest of the pipeline no more than one time
                bool nextCalled = false;
                var callNextOnce = async (List<LimitCheckResult> checkResults) => {
                    if (!nextCalled) 
                    {
                        nextCalled = true;

                        // Placing current checkResults into context.Items under a predefined key
                        context.Items[LimitCheckResultsContextKey] = checkResults;

                        await next();
                    }
                };

                var checkList = await this.IsIngressOrEgressExceededAsync(requestProxy, cleanupRoutines, callNextOnce);

                await this.ConstructResponse(context, checkList, requestProxy, callNextOnce);
            }
            finally
            {
                await Task.WhenAll(cleanupRoutines.Select(f => f()));
            }
        }

        private async Task ConstructResponse(HttpContext context, List<LimitCheckResult> checkList, IHttpRequestProxy requestProxy, Func<List<LimitCheckResult>, Task> callNextOnce)
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
                    await callNextOnce(checkList);
                }
            }
        }
    }

    /// <summary>
    /// Extension methods for configuring ThrottlingTrollMiddleware.
    /// </summary>
    public static class ThrottlingTrollExtensions
    {
        /// <summary>
        /// Configures ThrottlingTroll ingress throttling
        /// </summary>
        public static IFunctionsWorkerApplicationBuilder UseThrottlingTroll(this IFunctionsWorkerApplicationBuilder builder, Action<ThrottlingTrollOptions> options = null)
        {
            var opt = new ThrottlingTrollOptions();

            if (options != null)
            {
                options(opt);
            }

            // Need to create a single instance, yet still allow for multiple copies of ThrottlingTrollMiddleware with different settings
            ThrottlingTrollMiddleware throttlingTrollMiddleware = null;

            if (opt.GetConfigFunc == null)
            {
                if (opt.Config == null)
                {
                    // Trying to read config from settings.
                    var config = builder.Services.BuildServiceProvider().GetService<IConfiguration>();

                    var section = config?.GetSection(ConfigSectionName);

                    var throttlingTrollConfig = section?.Get<ThrottlingTrollConfig>();

                    if (throttlingTrollConfig == null)
                    {
                        throw new InvalidOperationException($"Failed to initialize ThrottlingTroll. Settings section '{ConfigSectionName}' not found or cannot be deserialized.");
                    }

                    opt.GetConfigFunc = async () => throttlingTrollConfig;
                }
                else
                {
                    opt.GetConfigFunc = async () => opt.Config;
                }
            }

            return builder.UseWhen
            (
                (FunctionContext context) =>
                {
                    // This middleware is only for http trigger invocations.
                    return context
                        .FunctionDefinition
                        .InputBindings
                        .Values
                        .First(a => a.Type.EndsWith("Trigger"))
                        .Type == "httpTrigger";
                },
                async (FunctionContext context, Func<Task> next) =>
                {
                    if (throttlingTrollMiddleware == null)
                    {
                        // Using opt as lock object.
                        lock (opt)
                        {
                            if (throttlingTrollMiddleware == null)
                            {

                                if (opt.Log == null)
                                {
                                    var logger = context.InstanceServices.GetService<ILogger<ThrottlingTroll>>();
                                    opt.Log = logger == null ? null : (l, s) => logger.Log(l, s);
                                }

                                if (opt.CounterStore == null)
                                {
                                    opt.CounterStore = context.GetOrCreateThrottlingTrollCounterStore();
                                }

                                throttlingTrollMiddleware = new ThrottlingTrollMiddleware(opt);
                            }
                        }
                    }
                    await throttlingTrollMiddleware.Invoke(context, next);
                }
            );
        }

        private static ICounterStore GetOrCreateThrottlingTrollCounterStore(this FunctionContext context)
        {
            var counterStore = context.InstanceServices.GetService<ICounterStore>();

            if (counterStore == null)
            {
                counterStore = new MemoryCacheCounterStore();
            }

            return counterStore;
        }

        private const string ConfigSectionName = "ThrottlingTrollIngress";
    }
}
