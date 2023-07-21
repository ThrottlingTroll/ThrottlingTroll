using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
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
    public class ThrottlingTrollMiddleware : ThrottlingTroll
    {
        private readonly RequestDelegate _next;

        private readonly Func<LimitExceededResult, IHttpRequestProxy, IHttpResponseProxy, CancellationToken, Task> _responseFabric;

        /// <summary>
        /// Ctor. Shold not be used externally, but needs to be public.
        /// </summary>
        public ThrottlingTrollMiddleware
        (
            RequestDelegate next,
            ThrottlingTrollOptions options

        ) : base(options.Log, options.CounterStore, options.GetConfigFunc, options.IntervalToReloadConfigInSeconds)
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
                        await this._next(context);
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

                    await this._responseFabric(result, requestProxy, responseProxy, context.RequestAborted);

                    if (responseProxy.ShouldContinueAsNormal)
                    {
                        // Continue with normal request processing

                        if (!restOfPipelineCalled)
                        {
                            await this._next(context);
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
    /// Extension methods for configuring ThrottlingTrollMiddleware
    /// </summary>
    public static class ThrottlingTrollExtensions
    {
        /// <summary>
        /// Configures ThrottlingTroll ingress throttling
        /// </summary>
        public static IApplicationBuilder UseThrottlingTroll(this IApplicationBuilder builder, Action<ThrottlingTrollOptions> options = null)
        {
            var opt = new ThrottlingTrollOptions();

            if (options != null)
            {
                options(opt);
            }

            if (opt.GetConfigFunc == null)
            {
                if (opt.Config == null)
                {
                    // Trying to read config from settings
                    var config = builder.ApplicationServices.GetService<IConfiguration>();

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
                var logger = builder.ApplicationServices.GetService<ILogger<ThrottlingTroll>>();
                opt.Log = logger == null ? null : (l, s) => logger.Log(l, s);
            }


            if (opt.CounterStore == null)
            {
                opt.CounterStore = builder.GetOrCreateThrottlingTrollCounterStore();
            }

            return builder.UseMiddleware<ThrottlingTrollMiddleware>(opt);
        }

        private static ICounterStore GetOrCreateThrottlingTrollCounterStore(this IApplicationBuilder builder)
        {
            var counterStore = builder.ApplicationServices.GetService<ICounterStore>();

            if (counterStore == null)
            {
                counterStore = new MemoryCacheCounterStore();
            }

            return counterStore;
        }

        private const string ConfigSectionName = "ThrottlingTrollIngress";
    }
}