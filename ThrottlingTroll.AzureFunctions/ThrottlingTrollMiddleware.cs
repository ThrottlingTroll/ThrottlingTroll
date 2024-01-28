using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
                bool nextCalled = false;
                var callNextOnce = async (List<LimitCheckResult> checkResults) => {
                    if (!nextCalled)
                    {
                        nextCalled = true;

                        // Placing current checkResults into context.Items under a predefined key
                        if (request.FunctionContext.Items.ContainsKey(LimitCheckResultsContextKey))
                        {
                            ((List<LimitCheckResult>)request.FunctionContext.Items[LimitCheckResultsContextKey]).AddRange(checkResults);
                        }
                        else
                        {
                            request.FunctionContext.Items[LimitCheckResultsContextKey] = checkResults;
                        }

                        await next();
                    }
                };

                var checkList = await this.IsIngressOrEgressExceededAsync(requestProxy, cleanupRoutines, callNextOnce);

                return await this.ConstructResponse(request, checkList, requestProxy, callNextOnce, cancellationToken);
            }
            finally
            {
                await Task.WhenAll(cleanupRoutines.Select(f => f()));
            }
        }

        private async Task<HttpResponseData> ConstructResponse(HttpRequestData request, List<LimitCheckResult> checkList, IHttpRequestProxy requestProxy, Func<List<LimitCheckResult>, Task> callNextOnce, CancellationToken cancellationToken)
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
                    await callNextOnce(checkList);

                    return null;
                }
                else
                {
                    return response;
                }
            }
        }
    }

    /// <summary>
    /// Extension methods for configuring ThrottlingTrollMiddleware
    /// </summary>
    public static class ThrottlingTrollExtensions
    {
        /// <summary>
        /// Configures ThrottlingTroll ingress throttling
        /// </summary>
        public static IFunctionsWorkerApplicationBuilder UseThrottlingTroll(this IFunctionsWorkerApplicationBuilder builder, HostBuilderContext builderContext, Action<ThrottlingTrollOptions> options = null)
        {
            return builder.UseThrottlingTroll(builderContext, options == null ? null : (ctx, opt) => options(opt));
        }

        /// <summary>
        /// Configures ThrottlingTroll ingress throttling
        /// </summary>
        public static IFunctionsWorkerApplicationBuilder UseThrottlingTroll(this IFunctionsWorkerApplicationBuilder builder, HostBuilderContext builderContext, Action<FunctionContext, ThrottlingTrollOptions> options)
        {
            // Need to create a single instance, yet still allow for multiple copies of ThrottlingTrollMiddleware with different settings
            var lockObject = new object();
            ThrottlingTrollMiddleware middleware = null;

            builder.UseWhen
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
                    // To initialize ThrottlingTrollMiddleware we need access to context.InstanceServices (the DI container),
                    // and it is only here when we get one.
                    // So that's why all the complexity with double-checked locking etc.

                    if (middleware == null)
                    {
                        // Using opt as lock object
                        lock (lockObject)
                        {
                            if (middleware == null)
                            {
                                var opt = new ThrottlingTrollOptions();

                                if (options != null)
                                {
                                    options(context, opt);
                                }

                                middleware = CreateMiddleware(context, opt);
                            }
                        }
                    }

                    var request = await context.GetHttpRequestDataAsync() ?? throw new ArgumentNullException("HTTP Request is null");

                    var response = await middleware.InvokeAsync(request, next, context.CancellationToken);

                    if (response != null)
                    {
                        context.GetInvocationResult().Value = response;
                    }
                }
             );

            return builder;
        }

        /// <summary>
        /// Configures ThrottlingTroll ingress throttling
        /// </summary>
        public static IHostBuilder UseThrottlingTroll(this IHostBuilder hostBuilder, Action<ThrottlingTrollOptions> options = null)
        {
            return hostBuilder.ConfigureFunctionsWorkerDefaults((HostBuilderContext builderContext, IFunctionsWorkerApplicationBuilder builder) =>
            {
                builder.UseThrottlingTroll(builderContext, options);
            });
        }

        /// <summary>
        /// Configures ThrottlingTroll ingress throttling
        /// </summary>
        public static IHostBuilder UseThrottlingTroll(this IHostBuilder hostBuilder, Action<FunctionContext, ThrottlingTrollOptions> options)
        {
            return hostBuilder.ConfigureFunctionsWorkerDefaults((HostBuilderContext builderContext, IFunctionsWorkerApplicationBuilder builder) =>
            {
                builder.UseThrottlingTroll(builderContext, options);
            });
        }

        private static ThrottlingTrollMiddleware CreateMiddleware(FunctionContext context, ThrottlingTrollOptions opt)
        {
            if (opt.GetConfigFunc == null)
            {
                if (opt.Config == null)
                {
                    // Trying to read config from settings.
                    var config = context.InstanceServices.GetService<IConfiguration>();

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

            if (opt.Log == null)
            {
                var logger = context.InstanceServices.GetService<ILogger<ThrottlingTroll>>();
                opt.Log = logger == null ? null : (l, s) => logger.Log(l, s);
            }

            if (opt.CounterStore == null)
            {
                opt.CounterStore = context.GetOrCreateThrottlingTrollCounterStore();
            }

            return new ThrottlingTrollMiddleware(opt);
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
