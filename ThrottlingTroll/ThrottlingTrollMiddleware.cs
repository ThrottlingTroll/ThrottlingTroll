using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using StackExchange.Redis;
using System;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// Implements ingress throttling
    /// </summary>
    public class ThrottlingTrollMiddleware : ThrottlingTroll
    {
        private readonly RequestDelegate _next;

        public ThrottlingTrollMiddleware
        (
            RequestDelegate next,
            ICounterStore counterStore,
            Action<LogLevel, string> log,
            Func<Task<ThrottlingTrollConfig>> getConfigFunc,
            int intervalToReloadConfigInSeconds

        ) : base(log, counterStore, getConfigFunc, intervalToReloadConfigInSeconds)
        {
            this._next = next;
        }

        /// <summary>
        /// Is invoked by ASP.NET middleware pipeline. Handles ingress throttling.
        /// </summary>
        public async Task InvokeAsync(HttpContext context)
        {
            int retryAfter = await this.IsExceededAsync(new HttpRequestProxy(context.Request));

            string retryAfterHeaderValue = null;

            if (retryAfter > 0)
            {
                retryAfterHeaderValue = retryAfter.ToString();
            }
            else
            {
                try
                {
                    await this._next(context);
                }
                catch (ThrottlingTrollTooManyRequestsException throttlingEx)
                {
                    // Catching propagated exception from egress
                    retryAfterHeaderValue = throttlingEx.RetryAfterHeaderValue;
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
                            retryAfterHeaderValue = throttlingEx.RetryAfterHeaderValue;
                            break;
                        }
                    }

                    if (throttlingEx == null)
                    {
                        throw;
                    }
                }
            }

            if (!string.IsNullOrEmpty(retryAfterHeaderValue))
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                context.Response.Headers.Add(HeaderNames.RetryAfter, retryAfterHeaderValue);

                string responseString = DateTime.TryParse(retryAfterHeaderValue, out var dt) ?
                    retryAfterHeaderValue :
                    $"{retryAfterHeaderValue} seconds";

                await context.Response.WriteAsync($"Retry after {responseString}");
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

            return builder.UseMiddleware<ThrottlingTrollMiddleware>(opt.CounterStore, opt.Log, opt.GetConfigFunc, opt.IntervalToReloadConfigInSeconds);
        }

        private static ICounterStore GetOrCreateThrottlingTrollCounterStore(this IApplicationBuilder builder)
        {
            var counterStore = builder.ApplicationServices.GetService<ICounterStore>();

            if (counterStore == null)
            {
                var redis = builder.ApplicationServices.GetService<IConnectionMultiplexer>();

                if (redis != null)
                {
                    counterStore = new RedisCounterStore(redis);
                }
                else
                {
                    var distributedCache = builder.ApplicationServices.GetService<IDistributedCache>();

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