using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// Main functionality. The same for both ingress and egress.
    /// </summary>
    public class ThrottlingTrollCore : IDisposable
    {
        #region Telemetry
        internal static readonly ActivitySource ActivitySource = new ActivitySource("ThrottlingTroll");

        internal static readonly Meter Meter = new Meter("ThrottlingTroll");

        internal static readonly Counter<int> IngressRuleMatchesCounter = 
            Meter.CreateCounter<int>("throttlingtroll.ingress.rules_matched", "Counts how many rate limiting rules were matched by incoming requests");
        internal static readonly Counter<int> IngressRequestsThrottledCounter = 
            Meter.CreateCounter<int>("throttlingtroll.ingress.requests_throttled", "Counts how many incoming requests were throttled (hit the limit)");

        internal static readonly Counter<int> EgressRuleMatchesCounter = 
            Meter.CreateCounter<int>("throttlingtroll.egress.rules_matched", "Counts how many rate limiting rules were matched by outgoing requests");
        internal static readonly Counter<int> EgressRequestsThrottledCounter = 
            Meter.CreateCounter<int>("throttlingtroll.egress.requests_throttled", "Counts how many outgoing requests were throttled (hit the limit)");

        internal static readonly Counter<int> FailuresCounter = 
            Meter.CreateCounter<int>("throttlingtroll.internal_failures", "Counts how many internal failures ThrottlingTroll experienced");
        internal static readonly Counter<int> GetConfigFuncSuccessesCounter = 
            Meter.CreateCounter<int>("throttlingtroll.get_config_func_successes", "Counts how many times ThrottlingTrollOptions.GetConfigFunc was successfully called");
        internal static readonly Counter<int> GetConfigFuncFailuresCounter = 
            Meter.CreateCounter<int>("throttlingtroll.get_config_func_failures", "Counts how many times ThrottlingTrollOptions.GetConfigFunc failed");
        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="ThrottlingTrollCore"/> class.
        /// </summary>
        /// <param name="log">Logging routine.</param>
        /// <param name="counterStore">An instance of <see cref="ICounterStore"/> to be used.</param>
        /// <param name="getConfigFunc"><see cref="ThrottlingTrollConfig"/> getter. Will be called every intervalToReloadConfigInSeconds.</param>
        /// <param name="identityIdExtractor">Identity ID extraction routine to be used for extracting Identity IDs from requests.</param>
        /// <param name="costExtractor">Request's cost extraction routine. The default cost (weight) of a request is 1, but this routine allows to override that.</param>
        /// <param name="intervalToReloadConfigInSeconds">When set to > 0, getConfigFunc will be called periodically with this interval. This allows you to dynamically change throttling rules and limits without restarting the service.</param>
        public ThrottlingTrollCore
        (
            Action<LogLevel, string> log,
            ICounterStore counterStore,
            Func<Task<ThrottlingTrollConfig>> getConfigFunc,
            Func<IHttpRequestProxy, string> identityIdExtractor,
            Func<IHttpRequestProxy, long> costExtractor,
            int intervalToReloadConfigInSeconds = 0
        )
        {
            ArgumentNullException.ThrowIfNull(counterStore);
            ArgumentNullException.ThrowIfNull(getConfigFunc);

            this._identityIdExtractor = identityIdExtractor;
            this._costExtractor = costExtractor;
            this._counterStore = counterStore;
            this._log = log ?? ((l, s) => { });
            this._counterStore.Log = this._log;

            this.InitGetConfigTask(getConfigFunc, intervalToReloadConfigInSeconds);
        }

        /// <summary>
        /// A key under which a <see cref="List{LimitCheckResult}"/> will be placed to HttpContext.Items or FunctionContext.Items
        /// </summary>
        public static readonly string LimitCheckResultsContextKey = "ThrottlingTrollLimitCheckResultsContextKey";

        /// <summary>
        /// A key under which a <see cref="List{ThrottlingTrollConfig}"/> will be placed to HttpContext.Items or FunctionContext.Items
        /// </summary>
        public static readonly string ThrottlingTrollConfigsContextKey = "ThrottlingTrollConfigsContextKey";

        /// <summary>
        /// Marks this instance as disposed
        /// </summary>
        public void Dispose()
        {
            this._disposed = true;
        }

        /// <summary>
        /// Returns the current <see cref="ThrottlingTrollConfig"/> snapshot
        /// </summary>
        protected async Task<ThrottlingTrollConfig> GetCurrentConfig()
        {
            // Always getting the current value from the task. Once completed, tasks cache and return the same resulting object anyway.
            var config = await this._getConfigTask;

            // If it is the same object, just returning it
            if (config == this._currentConfig)
            {
                return config;
            }

            // The object has changed (probably, because the task was re-executed), so we need to re-apply global settings to it

            if (config.Rules != null)
            {
                foreach (var rule in config.Rules)
                {
                    rule.IdentityIdExtractor ??= this._identityIdExtractor;

                    rule.CostExtractor ??= this._costExtractor;
                }
            }

            if (config.AllowList != null)
            {
                foreach (var rule in config.AllowList)
                {
                    rule.IdentityIdExtractor ??= this._identityIdExtractor;
                }
            }

            // This must be done at the very end of this method
            this._currentConfig = config;
            return config;
        }

        /// <summary>
        /// Checks if ingress limit is exceeded for a given request.
        /// Also checks whether there're any <see cref="ThrottlingTrollTooManyRequestsException"/>s from egress.
        /// Returns a list of check results for rules that this request matched.
        /// </summary>
        protected internal async Task<List<LimitCheckResult>> IsIngressOrEgressExceededAsync(IHttpRequestProxy request, List<Func<Task>> cleanupRoutines, Func<Task> nextAction)
        {
            #region Telemetry
            // Important to create this activity _before_ calling IsExceededAsync
            using var activity = ActivitySource.StartActivity("ThrottlingTroll.Ingress");
            #endregion

            // First trying ingress
            var checkList = await this.IsExceededAsync(request, cleanupRoutines);

            #region Telemetry
            foreach (var checkResult in checkList)
            {
                IngressRuleMatchesCounter.Add(1, new TagList { { "throttlingtroll.rule", checkResult.Rule.GetNameForTelemetry() }, { "throttlingtroll.counter_id", checkResult.CounterId } });

                if (checkResult.RequestsRemaining < 0)
                {
                    string msg = "Request limit exceeded";
                    activity?.AddTag("Result", msg);
                    activity?.SetStatus(ActivityStatusCode.Error, msg);

                    IngressRequestsThrottledCounter.Add(1, new TagList { { "throttlingtroll.rule", checkResult.Rule.GetNameForTelemetry() }, { "throttlingtroll.counter_id", checkResult.CounterId } });
                }
            }
            #endregion

            if (checkList.Any(r => r.RequestsRemaining < 0))
            {
                // If limit exceeded, returning immediately
                return checkList;
            }

            // Also trying to propagate egress to ingress
            try
            {
                await nextAction();

                #region Telemetry
                string msg = "Request processed successfully";
                activity?.AddTag("Result", msg);
                activity?.SetStatus(ActivityStatusCode.Ok);
                #endregion
            }
            catch (ThrottlingTrollTooManyRequestsException throttlingEx)
            {
                // Catching propagated exception from egress
                checkList.Add(new LimitCheckResult(throttlingEx.RetryAfterHeaderValue));

                #region Telemetry
                activity?.AddTag("RetryAfterHeaderFromEgress", throttlingEx.RetryAfterHeaderValue);
                string msg = "TooManyRequests error propagated from egress";
                activity?.AddTag("Result", msg);
                activity?.SetStatus(ActivityStatusCode.Error, msg);
                #endregion
            }
            catch (AggregateException ex)
            {
                // Catching propagated exception from egress as AggregateException
                // TODO: refactor to LINQ

                ThrottlingTrollTooManyRequestsException throttlingEx = null;

                foreach (var exx in ex.Flatten().InnerExceptions)
                {
                    throttlingEx = exx as ThrottlingTrollTooManyRequestsException;
                    if (throttlingEx != null)
                    {
                        checkList.Add(new LimitCheckResult(throttlingEx.RetryAfterHeaderValue));

                        #region Telemetry
                        activity?.AddTag("RetryAfterHeaderFromEgress", throttlingEx.RetryAfterHeaderValue);
                        string msg = "TooManyRequests error propagated from egress";
                        activity?.AddTag("Result", msg);
                        activity?.SetStatus(ActivityStatusCode.Error, msg);
                        #endregion

                        break;
                    }
                }

                if (throttlingEx == null)
                {
                    throw;
                }
            }

            return checkList;
        }

        /// <summary>
        /// Checks which limits were exceeded for a given request.
        /// Returns a list of check results for rules that this request matched.
        /// </summary>
        protected internal async Task<List<LimitCheckResult>> IsExceededAsync(IHttpRequestProxy request, List<Func<Task>> cleanupRoutines)
        {
            var result = new List<LimitCheckResult>();

            ThrottlingTrollConfig config;
            try
            {
                config = await this.GetCurrentConfig();
            }
            catch (Exception ex)
            {
                this._log(LogLevel.Error, $"ThrottlingTroll failed to get its config. {ex}");

                return result;
            }

            // Adding the current ThrottlingTrollConfig, for client's reference. Doing this _before_ any extractors are called.
            this.AppendToContextItem(request, ThrottlingTrollConfigsContextKey, new List<ThrottlingTrollConfig> { config });

            if (config.Rules == null)
            {
                return result;
            }

            bool allowListed = false;
            var dtStart = DateTimeOffset.UtcNow;

            // Still need to check all limits, so that all counters get updated
            foreach (var limit in config.Rules)
            {
                #region Telemetry
                using var activity = ActivitySource.StartActivity($"ThrottlingTroll.Rule.{limit.GetNameForTelemetry()}");
                limit.AddTagsToActivity(activity);
                #endregion

                try
                {
                    // First checking if request allowlisted
                    if (!limit.IgnoreAllowList && config.AllowList != null && config.AllowList.Any(filter => filter.IsMatch(request)))
                    {
                        allowListed = true;

                        #region Telemetry
                        string msg = "Request allowlisted";
                        activity?.AddTag("Result", msg);
                        activity?.SetStatus(ActivityStatusCode.Ok, msg);
                        #endregion

                        continue;
                    }

                    long requestCost = limit.GetCost(request);

                    var limitCheckResult = await limit.IsExceededAsync(request, requestCost, this._counterStore, config.UniqueName, this._log);

                    if (limitCheckResult == null)
                    {
                        // The request did not match this rule

                        #region Telemetry
                        string msg = "Request did not match this rule";
                        activity?.AddTag("Result", msg);
                        activity?.SetStatus(ActivityStatusCode.Ok, msg);
                        #endregion

                        continue;
                    }

                    #region Telemetry
                    activity?.AddTag("RequestCost", requestCost);
                    limitCheckResult.AddTagsToActivity(activity);
                    #endregion

                    if ((limitCheckResult.RequestsRemaining < 0) && limit.MaxDelayInSeconds > 0)
                    {
                        #region Telemetry
                        using var waitingActivity = ActivitySource.StartActivity($"ThrottlingTroll.Waiting");
                        #endregion

                        // Doing the delay logic
                        while ((DateTimeOffset.UtcNow - dtStart).TotalSeconds <= limit.MaxDelayInSeconds)
                        {
                            if (!await limit.IsStillExceededAsync(this._counterStore, limitCheckResult.CounterId, request))
                            {
                                // Doing double-check
                                limitCheckResult = await limit.IsExceededAsync(request, requestCost, this._counterStore, config.UniqueName, this._log);

                                if (limitCheckResult.RequestsRemaining >= 0)
                                {
                                    break;
                                }
                            }

                            await Task.Delay(this._sleepTimeSpan);
                        }

                        #region Telemetry
                        if (limitCheckResult.RequestsRemaining >= 0)
                        {
                            waitingActivity?.SetStatus(ActivityStatusCode.Ok, "Waiting finished");
                        }
                        else
                        {
                            waitingActivity?.SetStatus(ActivityStatusCode.Error, "Waiting timed out");
                        }
                        #endregion
                    }

                    if (limitCheckResult.RequestsRemaining >= 0)
                    {
                        // Decrementing this counter at the end of request processing
                        cleanupRoutines.Add(() => limit.OnRequestProcessingFinished(this._counterStore, limitCheckResult.CounterId, requestCost, this._log, request));
                    }

                    result.Add(limitCheckResult);

                    #region Telemetry
                    if (limitCheckResult.RequestsRemaining >= 0)
                    {
                        string msg = $"Requests remaining: {limitCheckResult.RequestsRemaining}";
                        activity?.AddTag("Result", msg);
                        activity?.SetStatus(ActivityStatusCode.Ok, msg);

                        activity?.SetStatus(ActivityStatusCode.Ok);
                    }
                    else
                    {
                        string msg = "Request limit exceeded";
                        activity?.AddTag("Result", msg);
                        activity?.SetStatus(ActivityStatusCode.Error, msg);
                    }
                    #endregion
                }
                catch (Exception ex)
                {
                    string msg = $"ThrottlingTroll failed. {ex}";
                    this._log(LogLevel.Error, msg);

                    #region Telemetry
                    activity?.AddTag("Result", msg);
                    activity?.SetStatus(ActivityStatusCode.Error, msg);

                    FailuresCounter.Add(1, new TagList { { "throttlingtroll.rule", limit.GetNameForTelemetry() } });
                    #endregion

                    if (limit.ShouldThrowOnFailures)
                    {
                        throw;
                    }
                }
            }

            if (allowListed)
            {
                this._log(LogLevel.Information, $"ThrottlingTroll allowlisted {request.Method} {request.UriWithoutQueryString}");
            }

            // Adding check results as an item to the HttpContext, so that client's code can use it
            this.AppendToContextItem(request, LimitCheckResultsContextKey, result);

            return result;
        }

        /// <summary>
        /// Applies the CircuitBreaker logic
        /// </summary>
        protected internal async Task CheckAndBreakTheCircuit(IHttpRequestProxy request, IHttpResponseProxy response, Exception exception)
        {
            ThrottlingTrollConfig config;
            try
            {
                config = await this.GetCurrentConfig();
            }
            catch (Exception ex)
            {
                this._log(LogLevel.Error, $"ThrottlingTroll failed to get its config. {ex}");

                return;
            }

            if (config?.Rules == null)
            {
                return;
            }

            // Checking all CircuitBreaker rules that this request matches
            foreach (var limit in config.Rules)
            {
                #region Telemetry
                Activity activity = null;
                #endregion

                try
                {
                    var circuitBreakerMethod = limit.LimitMethod as CircuitBreakerRateLimitMethod;
                    if (circuitBreakerMethod == null || !limit.IsMatch(request))
                    {
                        continue;
                    }

                    string uniqueCacheKey = limit.GetUniqueCacheKey(request, config.UniqueName);

                    #region Telemetry
                    activity = ActivitySource.StartActivity($"ThrottlingTroll.CircuitBreakerRule.{limit.GetNameForTelemetry()}");
                    activity?.AddTag($"CounterId", uniqueCacheKey);
                    limit.AddTagsToActivity(activity);
                    #endregion

                    if (circuitBreakerMethod.IsFailed(response, exception))
                    {
                        // Checking the failure count

                        var now = DateTimeOffset.UtcNow;

                        var ttl = now - TimeSpan.FromMilliseconds(now.Millisecond) + TimeSpan.FromSeconds(circuitBreakerMethod.IntervalInSeconds);

                        // Better to use a separate counter for failures, because Trial mode increments the counter regardless of the output.
                        // We don't want to count successes as failures, that would distort the picture.
                        string failureCountCacheKey = $"{uniqueCacheKey}|CircuitBreakerFailureCount";

                        long failureCount = await this._counterStore.IncrementAndGetAsync(
                            failureCountCacheKey,
                            cost: 1,
                            ttl.UtcTicks,
                            CounterStoreIncrementAndGetOptions.SetAbsoluteTtl,
                            maxCounterValueToSetTtl: 1,
                            request);

                        if (failureCount > circuitBreakerMethod.PermitLimit)
                        {
                            // If failure limit is exceeded, placing this limit into Trial mode, which limits request rate to 1 request per trial interval
                            CircuitBreakerRateLimitMethod.PutIntoTrial(uniqueCacheKey);

                            #region Telemetry
                            string msg = $"Went into trial mode";
                            activity?.AddTag("Result", msg);
                            activity?.AddTag($"FailureCount", failureCount);
                            activity?.SetStatus(ActivityStatusCode.Error, msg);
                            #endregion
                        }
                        else
                        {
                            #region Telemetry
                            string msg = $"Failure count so far: {failureCount}";
                            activity?.AddTag("Result", msg);
                            activity?.AddTag($"FailureCount", failureCount);
                            activity?.SetStatus(ActivityStatusCode.Ok, msg);
                            #endregion
                        }
                    }
                    else
                    {
                        // Just lifting the trial mode
                        CircuitBreakerRateLimitMethod.ReleaseFromTrial(uniqueCacheKey);

                        #region Telemetry
                        string msg = "Released from trial mode";
                        activity?.AddTag("Result", msg);
                        activity?.SetStatus(ActivityStatusCode.Ok, msg);
                        #endregion
                    }
                }
                catch (Exception ex)
                {
                    string msg = $"ThrottlingTroll failed. {ex}";
                    this._log(LogLevel.Error, msg);

                    #region Telemetry
                    activity?.AddTag("Result", msg);
                    activity?.SetStatus(ActivityStatusCode.Error, msg);
                    #endregion

                    if (limit.ShouldThrowOnFailures)
                    {
                        throw;
                    }
                }
                finally
                {
                    #region Telemetry
                    activity?.Dispose();
                    #endregion
                }
            }
        }

        /// <summary>
        /// Initializes this._getConfigTask and also makes it reinitialized every intervalToReloadConfigInSeconds
        /// </summary>
        private void InitGetConfigTask(Func<Task<ThrottlingTrollConfig>> getConfigFunc, int intervalToReloadConfigInSeconds)
        {
            if (this._disposed)
            {
                return;
            }

            if (intervalToReloadConfigInSeconds <= 0)
            {
                this._getConfigTask = getConfigFunc();

                return;
            }

            var task = getConfigFunc();

            task.ContinueWith(async t =>
            {
                #region Telemetry
                if (t.IsFaulted)
                {
                    GetConfigFuncFailuresCounter.Add(1);
                }
                else if (t.IsCompletedSuccessfully)
                {
                    GetConfigFuncSuccessesCounter.Add(1);
                }
                #endregion

                await Task.Delay(TimeSpan.FromSeconds(intervalToReloadConfigInSeconds));

                this.InitGetConfigTask(getConfigFunc, intervalToReloadConfigInSeconds);
            });

            this._getConfigTask = task;
        }

        private void AppendToContextItem<T>(IHttpRequestProxy request, string key, List<T> list)
        {
            try
            {
                // Adding check results as an item to the HttpContext, so that client's code can use it
                request.AppendToContextItem(key, list);
            }
            catch (ObjectDisposedException ex)
            {
                // At this point HttpContext might be already disposed, and in that case this happens.
                this._log(LogLevel.Warning, $"ThrottlingTroll failed to populate request context with LimitCheckResults. {ex}");
            }
        }

        private readonly Action<LogLevel, string> _log;
        private readonly ICounterStore _counterStore;
        private readonly Func<IHttpRequestProxy, string> _identityIdExtractor;
        private readonly Func<IHttpRequestProxy, long> _costExtractor;
        private Task<ThrottlingTrollConfig> _getConfigTask;
        private bool _disposed = false;
        private TimeSpan _sleepTimeSpan = TimeSpan.FromMilliseconds(50);

        /// <summary>
        /// Caching the current config value, so that we don't need to re-apply global settings every time
        /// </summary>
        private ThrottlingTrollConfig _currentConfig;
    }
}