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
        private readonly Func<LimitExceededResult, IHttpRequestProxy, IHttpResponseProxy, CancellationToken, Task> _responseFabric;

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
        public async Task Invoke(FunctionContext context, Func<Task> next)
        {
            await InvokeOnHttpContext(context.GetHttpContext(), next);
        }

        internal async Task InvokeOnHttpContext(HttpContext httpContext, Func<Task> next)
        {
            if (httpContext == null)
            {
                throw new InvalidOperationException("FunctionContext has no http context associated with it.");
            }

            var requestProxy = new IncomingHttpRequestProxy(httpContext.Request);
            var cleanupRoutines = new List<Func<Task>>();

            try
            {
                // First trying ingress
                var result = await this.IsExceededAsync(requestProxy, cleanupRoutines);

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

                if (this._responseFabric == null)
                {
                    httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                    // Formatting default Retry-After response

                    if (!string.IsNullOrEmpty(result.RetryAfterHeaderValue))
                    {
                        httpContext.Response.Headers.Add(HeaderNames.RetryAfter, result.RetryAfterHeaderValue);
                    }

                    string responseString = DateTime.TryParse(result.RetryAfterHeaderValue, out var dt) ?
                        result.RetryAfterHeaderValue :
                        $"{result.RetryAfterHeaderValue} seconds";

                    await httpContext.Response.WriteAsync($"Retry after {responseString}");
                }
                else
                {
                    // Using the provided response builder
                    var responseProxy = new IngressHttpResponseProxy(httpContext.Response);

                    await this._responseFabric(result, requestProxy, responseProxy, httpContext.RequestAborted);

                    if (responseProxy.ShouldContinueAsNormal)
                    {
                        // Continue with normal request processing

                        if (!restOfPipelineCalled)
                        {
                            await next();
                        }
                    }
                }
            }
            finally
            {
                await Task.WhenAll(cleanupRoutines.Select(f => f()));
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
