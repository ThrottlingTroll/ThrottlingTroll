using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// Implements egress throttling
    /// </summary>
    public class ThrottlingTrollHandler : DelegatingHandler
    {
        private readonly ThrottlingTroll _troll;
        private bool _propagateToIngress;

        /// <summary>
        /// Use this ctor when manually creating <see cref="HttpClient"/> instances. 
        /// </summary>
        /// <param name="counterStore">Implementation of <see cref="ICounterStore"/></param>
        /// <param name="config">Throttling configuration</param>
        /// <param name="log">Logging utility</param>
        /// <param name="innerHttpMessageHandler">Instance of <see cref="HttpMessageHandler"/> to use as inner handler. When null, a default <see cref="HttpClientHandler"/> instance will be created.</param>
        public ThrottlingTrollHandler
        (
            ThrottlingTrollEgressConfig config,
            Action<LogLevel, string> log = null,
            HttpMessageHandler innerHttpMessageHandler = null

        ) : this(new MemoryCacheCounterStore(), config, log, innerHttpMessageHandler)
        {
        }

        /// <summary>
        /// Use this ctor when manually creating <see cref="HttpClient"/> instances. 
        /// </summary>
        /// <param name="counterStore">Implementation of <see cref="ICounterStore"/></param>
        /// <param name="config">Throttling configuration</param>
        /// <param name="log">Logging utility</param>
        /// <param name="innerHttpMessageHandler">Instance of <see cref="HttpMessageHandler"/> to use as inner handler. When null, a default <see cref="HttpClientHandler"/> instance will be created.</param>
        public ThrottlingTrollHandler
        (
            ICounterStore counterStore,
            ThrottlingTrollEgressConfig config,
            Action<LogLevel, string> log = null,
            HttpMessageHandler innerHttpMessageHandler = null

        ) : base(innerHttpMessageHandler ?? new HttpClientHandler())
        {
            this._troll = new ThrottlingTroll(log, counterStore, async () => config);
            this._propagateToIngress = config.PropagateToIngress;
        }

        /// <summary>
        /// Use this ctor in DI container initialization (with <see cref="HttpClientBuilderExtensions.AddHttpMessageHandler"/>)
        /// </summary>
        /// <param name="counterStore">Implementation of <see cref="ICounterStore"/></param>
        /// <param name="log">Logging utility</param>
        /// <param name="getConfigFunc">Function that produces <see cref="ThrottlingTrollConfig"/></param>
        /// <param name="intervalToReloadConfigInSeconds">Interval to periodically call getConfigFunc. When 0, getConfigFunc will only be called once.</param>
        internal ThrottlingTrollHandler
        (
            ICounterStore counterStore,
            Action<LogLevel, string> log,
            Func<Task<ThrottlingTrollConfig>> getConfigFunc = null,
            int intervalToReloadConfigInSeconds = 0
        )
        {
            this._troll = new ThrottlingTroll(log, counterStore, async () =>
            {
                var config = await getConfigFunc();

                var egressConfig = config as ThrottlingTrollEgressConfig;
                if (egressConfig != null)
                {
                    // Need to also get this flag from config
                    this._propagateToIngress = egressConfig.PropagateToIngress;
                }

                return config;

            }, intervalToReloadConfigInSeconds);
        }

        /// <summary>
        /// Handles egress throttling.
        /// </summary>
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Decoupling from SynchronizationContext just in case
            var isExceededResult = Task.Run(() =>
            {
                return this._troll.IsExceededAsync(new HttpRequestProxy(request));
            })
            .Result;

            HttpResponseMessage response;

            if (isExceededResult == null)
            {
                response = base.Send(request, cancellationToken);
            }
            else
            {
                response = this.CreateRetryAfterResponse(request, isExceededResult);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests && this._propagateToIngress)
            {
                // Propagating TooManyRequests response up to ThrottlingTrollMiddleware
                throw new ThrottlingTrollTooManyRequestsException
                {
                    RetryAfterHeaderValue = response.Headers.RetryAfter.ToString()
                };
            }

            return response;
        }

        /// <summary>
        /// Handles egress throttling.
        /// </summary>
        protected async override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var isExceededResult = await this._troll.IsExceededAsync(new HttpRequestProxy(request));

            HttpResponseMessage response;

            if (isExceededResult == null)
            {
                response = await base.SendAsync(request, cancellationToken);
            }
            else
            {
                response = this.CreateRetryAfterResponse(request, isExceededResult);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests && this._propagateToIngress)
            {
                // Propagating TooManyRequests response up to ThrottlingTrollMiddleware
                throw new ThrottlingTrollTooManyRequestsException
                {
                    RetryAfterHeaderValue = response.Headers.RetryAfter.ToString()
                };
            }

            return response;
        }

        private HttpResponseMessage CreateRetryAfterResponse(HttpRequestMessage request, LimitExceededResult limitExceededResult)
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

            if (DateTime.TryParse(limitExceededResult.RetryAfterHeaderValue, out var retryAfterDateTime))
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfterDateTime);
                response.Content = new StringContent($"Retry after {limitExceededResult.RetryAfterHeaderValue}");
            }
            else if (int.TryParse(limitExceededResult.RetryAfterHeaderValue, out int retryAfterInSeconds))
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(retryAfterInSeconds));
                response.Content = new StringContent($"Retry after {retryAfterInSeconds} seconds");
            }

            response.RequestMessage = request;

            return response;
        }

        protected override void Dispose(bool disposing)
        {
            this._troll.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Extension methods for configuring ThrottlingTrollHandler
    /// </summary>
    public static class ThrottlingTrollHandlerExtensions
    {
        /// <summary>
        /// Appends <see cref="ThrottlingTrollHandler"/> to the given HttpClient.
        /// Optionally allows to configure options.
        /// </summary>
        public static IHttpClientBuilder AddThrottlingTrollMessageHandler(this IHttpClientBuilder builder, Action<ThrottlingTrollOptions> options = null)
        {
            var opt = new ThrottlingTrollOptions();

            if (options != null)
            {
                options(opt);
            }

            return builder.AddHttpMessageHandler(serviceProvider =>
            {
                if (opt.GetConfigFunc == null)
                {
                    if (opt.Config == null)
                    {
                        // Trying to read config from settings
                        var config = serviceProvider.GetService<IConfiguration>();

                        var section = config?.GetSection(ConfigSectionName);

                        var throttlingTrollConfig = section?.Get<ThrottlingTrollEgressConfig>();

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
                    var logger = serviceProvider.GetService<ILogger<ThrottlingTroll>>();

                    opt.Log = logger == null ? null : (l, s) => logger.Log(l, s);
                }

                if (opt.CounterStore == null)
                {
                    opt.CounterStore = serviceProvider.GetOrCreateThrottlingTrollCounterStore();
                }

                return new ThrottlingTrollHandler(opt.CounterStore, opt.Log, opt.GetConfigFunc, opt.IntervalToReloadConfigInSeconds);
            });
        }

        private static ICounterStore GetOrCreateThrottlingTrollCounterStore(this IServiceProvider serviceProvider)
        {
            var counterStore = serviceProvider.GetService<ICounterStore>();

            if (counterStore == null)
            {
                var redis = serviceProvider.GetService<IConnectionMultiplexer>();

                if (redis != null)
                {
                    counterStore = new RedisCounterStore(redis);
                }
                else
                {
                    var distributedCache = serviceProvider.GetService<IDistributedCache>();

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

        private const string ConfigSectionName = "ThrottlingTrollEgress";
    }
}