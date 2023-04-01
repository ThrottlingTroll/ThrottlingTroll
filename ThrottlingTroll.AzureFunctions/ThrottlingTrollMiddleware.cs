﻿using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// Implements ingress throttling for Azure Functions
    /// </summary>
    public class ThrottlingTrollMiddleware : ThrottlingTroll
    {
        private readonly Func<LimitExceededResult, IHttpRequestProxy, IHttpResponseProxy, CancellationToken, Task> _responseFabric;

        internal ThrottlingTrollMiddleware
        (
            ThrottlingTrollOptions options

        ) : base(options.Log, options.CounterStore, options.GetConfigFunc, options.IntervalToReloadConfigInSeconds)
        {
            this._responseFabric = options.ResponseFabric;
        }

        /// <summary>
        /// Is invoked by Azure Functions middleware pipeline. Handles ingress throttling.
        /// </summary>
        public async Task Invoke(FunctionContext context, Func<Task> next)
        {
            var request = await context.GetHttpRequestDataAsync() ?? throw new ArgumentNullException("HTTP Request is null");

            var requestProxy = new IncomingHttpRequestProxy(request);

            // First trying ingress
            var result = await this.IsExceededAsync(requestProxy);

            bool restOfPipelineCalled = false;

            if (result == null)
            {
                restOfPipelineCalled = true;

                // Also trying to propagate egress to ingress
                try
                {
                    await next();
                }
                catch (ThrottlingTrollTooManyRequestsException throttlingEx)
                {
                    // Catching propagated exception from egress
                    result = new LimitExceededResult(throttlingEx.RetryAfterHeaderValue);
                }
                catch (AggregateException ex)
                {
                    // Catching propagated exception from egress as AggregateException

                    ThrottlingTrollTooManyRequestsException throttlingEx = null;

                    foreach (var exx in ex.Flatten().InnerExceptions)
                    {
                        throttlingEx = exx as ThrottlingTrollTooManyRequestsException;
                        if (throttlingEx != null)
                        {
                            result = new LimitExceededResult(throttlingEx.RetryAfterHeaderValue);
                            break;
                        }
                    }

                    if (throttlingEx == null)
                    {
                        throw;
                    }
                }
            }

            if (result == null)
            {
                return;
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

                context.GetInvocationResult().Value = response;
            }
            else
            {
                // Using the provided response builder

                var responseProxy = new IngressHttpResponseProxy(response);

                await this._responseFabric(result, requestProxy, responseProxy, context.CancellationToken);

                if (responseProxy.ShouldContinueAsNormal)
                {
                    // Continue with normal request processing

                    if (!restOfPipelineCalled)
                    {
                        await next();
                    }
                }
                else
                {
                    context.GetInvocationResult().Value = response;
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
                    // Trying to read config from settings

                    var section = builderContext.Configuration?.GetSection(ConfigSectionName);

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

                    if (throttlingTrollMiddleware == null)
                    {
                        // Using opt as lock object
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

        private static ICounterStore GetOrCreateThrottlingTrollCounterStore(this FunctionContext context)
        {
            var counterStore = context.InstanceServices.GetService<ICounterStore>();

            if (counterStore == null)
            {
                var redis = context.InstanceServices.GetService<IConnectionMultiplexer>();

                if (redis != null)
                {
                    counterStore = new RedisCounterStore(redis);
                }
                else
                {
                    var distributedCache = context.InstanceServices.GetService<IDistributedCache>();

                    if (distributedCache != null)
                    {
                        counterStore = new DistributedCacheCounterStore(distributedCache);
                    }
                    else
                    {
                        // Defaulting to MemoryCacheCounterStore
                        counterStore = new MemoryCacheCounterStore();
                    }
                }
            }

            return counterStore;
        }

        private const string ConfigSectionName = "ThrottlingTrollIngress";
    }
}