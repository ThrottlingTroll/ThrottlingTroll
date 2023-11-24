using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
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
        private const int DefaultDelayInSeconds = 5;

        private readonly ThrottlingTroll _troll;
        private bool _propagateToIngress;

        private readonly Func<List<LimitCheckResult>, IHttpRequestProxy, IHttpResponseProxy, CancellationToken, Task> _responseFabric;

        /// <summary>
        /// Use this ctor when manually creating <see cref="HttpClient"/> instances. 
        /// </summary>
        /// <param name="config">Throttling configuration</param>
        /// <param name="log">Logging utility</param>
        /// <param name="innerHttpMessageHandler">Instance of <see cref="HttpMessageHandler"/> to use as inner handler. When null, a default <see cref="HttpClientHandler"/> instance will be created.</param>
        public ThrottlingTrollHandler
        (
            ThrottlingTrollEgressConfig config,
            Action<LogLevel, string> log = null,
            HttpMessageHandler innerHttpMessageHandler = null

        ) : this(null, config, log, innerHttpMessageHandler)
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

        ) : this(null, counterStore, config, log, innerHttpMessageHandler)
        {
        }

        /// <summary>
        /// Use this ctor when manually creating <see cref="HttpClient"/> instances. 
        /// </summary>
        /// <param name="responseFabric">Routine for generating custom HTTP responses and/or controlling built-in retries</param>
        /// <param name="counterStore">Implementation of <see cref="ICounterStore"/></param>
        /// <param name="config">Throttling configuration</param>
        /// <param name="log">Logging utility</param>
        /// <param name="innerHttpMessageHandler">Instance of <see cref="HttpMessageHandler"/> to use as inner handler. When null, a default <see cref="HttpClientHandler"/> instance will be created.</param>
        public ThrottlingTrollHandler
        (
            Func<List<LimitCheckResult>, IHttpRequestProxy, IHttpResponseProxy, CancellationToken, Task> responseFabric,
            ICounterStore counterStore,
            ThrottlingTrollEgressConfig config,
            Action<LogLevel, string> log = null,
            HttpMessageHandler innerHttpMessageHandler = null

        ) : this(null, responseFabric, counterStore, config, log, innerHttpMessageHandler)
        {
        }

        /// <summary>
        /// Use this ctor when manually creating <see cref="HttpClient"/> instances. 
        /// </summary>
        /// <param name="identityIdExtractor">Identity ID extraction routine to be used for extracting Identity IDs from requests</param>
        /// <param name="responseFabric">Routine for generating custom HTTP responses and/or controlling built-in retries</param>
        /// <param name="counterStore">Implementation of <see cref="ICounterStore"/></param>
        /// <param name="config">Throttling configuration</param>
        /// <param name="log">Logging utility</param>
        /// <param name="innerHttpMessageHandler">Instance of <see cref="HttpMessageHandler"/> to use as inner handler. When null, a default <see cref="HttpClientHandler"/> instance will be created.</param>
        public ThrottlingTrollHandler
        (
            Func<IHttpRequestProxy, string> identityIdExtractor,
            Func<List<LimitCheckResult>, IHttpRequestProxy, IHttpResponseProxy, CancellationToken, Task> responseFabric,
            ICounterStore counterStore,
            ThrottlingTrollEgressConfig config,
            Action<LogLevel, string> log = null,
            HttpMessageHandler innerHttpMessageHandler = null

        ) : this(null, identityIdExtractor, responseFabric, counterStore, config, log, innerHttpMessageHandler)
        {
        }

        /// <summary>
        /// Use this ctor when manually creating <see cref="HttpClient"/> instances. 
        /// </summary>
        /// <param name="costExtractor">Request's cost extraction routine</param>
        /// <param name="identityIdExtractor">Identity ID extraction routine to be used for extracting Identity IDs from requests</param>
        /// <param name="responseFabric">Routine for generating custom HTTP responses and/or controlling built-in retries</param>
        /// <param name="counterStore">Implementation of <see cref="ICounterStore"/></param>
        /// <param name="config">Throttling configuration</param>
        /// <param name="log">Logging utility</param>
        /// <param name="innerHttpMessageHandler">Instance of <see cref="HttpMessageHandler"/> to use as inner handler. When null, a default <see cref="HttpClientHandler"/> instance will be created.</param>
        public ThrottlingTrollHandler
        (
            Func<IHttpRequestProxy, long> costExtractor,
            Func<IHttpRequestProxy, string> identityIdExtractor,
            Func<List<LimitCheckResult>, IHttpRequestProxy, IHttpResponseProxy, CancellationToken, Task> responseFabric,
            ICounterStore counterStore,
            ThrottlingTrollEgressConfig config,
            Action<LogLevel, string> log = null,
            HttpMessageHandler innerHttpMessageHandler = null

        ) : base(innerHttpMessageHandler ?? new HttpClientHandler())
        {
            this._troll = new ThrottlingTroll(log, counterStore ?? new MemoryCacheCounterStore(), async () => config, identityIdExtractor, costExtractor);
            this._propagateToIngress = config.PropagateToIngress;
            this._responseFabric = responseFabric;
        }

        /// <summary>
        /// Use this ctor in DI container initialization (with <see cref="HttpClientBuilderExtensions.AddHttpMessageHandler"/>)
        /// </summary>
        internal ThrottlingTrollHandler(ThrottlingTrollOptions options)
        {
            this._responseFabric = options.ResponseFabric;

            this._troll = new ThrottlingTroll(options.Log, options.CounterStore, async () =>
            {
                var config = await options.GetConfigFunc();

                var egressConfig = config as ThrottlingTrollEgressConfig;
                if (egressConfig != null)
                {
                    // Need to also get this flag from config
                    this._propagateToIngress = egressConfig.PropagateToIngress;
                }

                return config;

            }, options.IdentityIdExtractor, options.CostExtractor, options.IntervalToReloadConfigInSeconds);
        }

        /// <summary>
        /// Handles egress throttling.
        /// </summary>
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestProxy = new OutgoingHttpRequestProxy(request);
            HttpResponseMessage response;
            int retryCount = 0;

            while (true)
            {
                var cleanupRoutines = new List<Func<Task>>();

                // Decoupling from SynchronizationContext just in case
                var checkList = Task.Run(() =>
                {
                    return this._troll.IsExceededAsync(requestProxy, cleanupRoutines);
                })
                .Result;

                var exceededRules = checkList
                    .Where(r => r.RequestsRemaining < 0)
                    // Sorting by the suggested RetryAfter header value (which is expected to be in seconds) in descending order
                    .OrderByDescending(r => r.RetryAfterInSeconds)
                    .ToArray();

                try
                {
                    if (exceededRules.Length <= 0)
                    {
                        // Just making the call as normal
                        response = base.Send(request, cancellationToken);
                    }
                    else
                    {
                        // Creating the TooManyRequests response
                        response = this.CreateRetryAfterResponse(request, exceededRules.First());
                    }
                }
                finally
                {
                    // Decrementing counters
                    Task.WaitAll(cleanupRoutines.Select(f => f()).ToArray());
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests && this._responseFabric != null)
                {
                    // Using custom response fabric
                    var responseProxy = new EgressHttpResponseProxy(response, retryCount++);

                    // Decoupling from SynchronizationContext just in case
                    Task.Run(() =>
                    {
                        return this._responseFabric(checkList, requestProxy, responseProxy, cancellationToken);
                    })
                    .Wait();

                    if (responseProxy.ShouldRetry)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        Thread.Sleep(this.GetRetryAfterTimeSpan(response.Headers));

                        cancellationToken.ThrowIfCancellationRequested();

                        // Retrying the call
                        continue;
                    }

                    response = responseProxy.Response;
                }

                break;
            }

            this.PropagateToIngressIfNeeded(response);

            return response;
        }

        /// <summary>
        /// Handles egress throttling.
        /// </summary>
        protected async override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestProxy = new OutgoingHttpRequestProxy(request);
            HttpResponseMessage response;
            int retryCount = 0;

            while (true)
            {
                var cleanupRoutines = new List<Func<Task>>();

                var checkList = await this._troll.IsExceededAsync(requestProxy, cleanupRoutines);

                var exceededRules = checkList
                    .Where(r => r.RequestsRemaining < 0)
                    // Sorting by the suggested RetryAfter header value (which is expected to be in seconds) in descending order
                    .OrderByDescending(r => r.RetryAfterInSeconds)
                    .ToArray();

                try
                {
                    if (exceededRules.Length <= 0)
                    {
                        // Just making the call as normal
                        response = await base.SendAsync(request, cancellationToken);
                    }
                    else
                    {
                        // Creating the TooManyRequests response
                        response = this.CreateRetryAfterResponse(request, exceededRules.First());
                    }
                }
                finally
                {
                    // Decrementing counters
                    await Task.WhenAll(cleanupRoutines.Select(f => f()));
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests && this._responseFabric != null)
                {
                    // Using custom response fabric
                    var responseProxy = new EgressHttpResponseProxy(response, retryCount++);

                    await this._responseFabric(checkList, requestProxy, responseProxy, cancellationToken);

                    if (responseProxy.ShouldRetry)
                    {
                        await Task.Delay(this.GetRetryAfterTimeSpan(response.Headers), cancellationToken);

                        // Retrying the call
                        continue;
                    }

                    response = responseProxy.Response;
                }

                break;
            }

            this.PropagateToIngressIfNeeded(response);

            return response;
        }

        private void PropagateToIngressIfNeeded(HttpResponseMessage response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests && this._propagateToIngress)
            {
                // Propagating TooManyRequests response up to ThrottlingTrollMiddleware
                throw new ThrottlingTrollTooManyRequestsException
                {
                    RetryAfterHeaderValue = response.Headers.RetryAfter.ToString()
                };
            }
        }

        private TimeSpan GetRetryAfterTimeSpan(HttpResponseHeaders headers)
        {
            if (headers?.RetryAfter?.Delta != null)
            {
                return headers.RetryAfter.Delta.Value;
            }
            else if (headers?.RetryAfter?.Date != null)
            {
                return headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
            }

            return TimeSpan.FromSeconds(DefaultDelayInSeconds);
        }

        private HttpResponseMessage CreateRetryAfterResponse(HttpRequestMessage request, LimitCheckResult limitExceededResult)
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

        /// <inheritdoc />
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

                return new ThrottlingTrollHandler(opt);
            });
        }

        private static ICounterStore GetOrCreateThrottlingTrollCounterStore(this IServiceProvider serviceProvider)
        {
            var counterStore = serviceProvider.GetService<ICounterStore>();

            if (counterStore == null)
            {
                counterStore = new MemoryCacheCounterStore();
            }

            return counterStore;
        }

        private const string ConfigSectionName = "ThrottlingTrollEgress";
    }
}