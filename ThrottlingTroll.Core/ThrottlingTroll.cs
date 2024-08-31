using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// Main functionality. The same for both ingress and egress.
    /// </summary>
    public class ThrottlingTroll : IDisposable
    {
        private readonly Action<LogLevel, string> _log;
        private readonly ICounterStore _counterStore;
        private readonly Func<IHttpRequestProxy, string> _identityIdExtractor;
        private readonly Func<IHttpRequestProxy, long> _costExtractor;
        private Task<ThrottlingTrollConfig> _getConfigTask;
        private bool _disposed = false;
        private TimeSpan _sleepTimeSpan = TimeSpan.FromMilliseconds(50);

        /// <summary>
        /// Ctor
        /// </summary>
        protected internal ThrottlingTroll
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
                    if (rule.IdentityIdExtractor == null)
                    {
                        rule.IdentityIdExtractor = this._identityIdExtractor;
                    }
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
            // First trying ingress
            var checkList = await this.IsExceededAsync(request, cleanupRoutines);

            if (checkList.Any(r => r.RequestsRemaining < 0))
            {
                // If limit exceeded, returning immediately
                return checkList;
            }

            // Also trying to propagate egress to ingress
            try
            {
                await nextAction();
            }
            catch (ThrottlingTrollTooManyRequestsException throttlingEx)
            {
                // Catching propagated exception from egress
                checkList.Add(new LimitCheckResult(throttlingEx.RetryAfterHeaderValue));
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
                try
                {
                    // First checking if request allowlisted
                    if (!limit.IgnoreAllowList && config.AllowList != null && config.AllowList.Any(filter => filter.IsMatch(request)))
                    {
                        allowListed = true;
                        continue;
                    }

                    long requestCost = limit.GetCost(request);

                    var limitCheckResult = await limit.IsExceededAsync(request, requestCost, this._counterStore, config.UniqueName, this._log);

                    if (limitCheckResult == null)
                    {
                        // The request did not match this rule
                        continue;
                    }

                    if ((limitCheckResult.RequestsRemaining < 0) && limit.MaxDelayInSeconds > 0)
                    {
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
                    }

                    if (limitCheckResult.RequestsRemaining >= 0)
                    {
                        // Decrementing this counter at the end of request processing
                        cleanupRoutines.Add(() => limit.OnRequestProcessingFinished(this._counterStore, limitCheckResult.CounterId, requestCost, this._log, request));
                    }

                    result.Add(limitCheckResult);
                }
                catch (Exception ex)
                {
                    this._log(LogLevel.Error, $"ThrottlingTroll failed. {ex}");

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
                try
                {
                    var circuitBreakerMethod = limit.LimitMethod as CircuitBreakerRateLimitMethod;
                    if (circuitBreakerMethod == null || !limit.IsMatch(request))
                    {
                        continue;
                    }

                    string uniqueCacheKey = limit.GetUniqueCacheKey(request, config.UniqueName);

                    if (circuitBreakerMethod.IsFailed(response, exception))
                    {
                        // Checking the failure count

                        var now = DateTime.UtcNow;

                        var ttl = now - TimeSpan.FromMilliseconds(now.Millisecond) + TimeSpan.FromSeconds(circuitBreakerMethod.IntervalInSeconds);

                        // Better to use a separate counter for failures, because Trial mode increments the counter regardless of the output.
                        // We don't want to count successes as failures, that would distort the picture.
                        string failureCountCacheKey = $"{uniqueCacheKey}|CircuitBreakerFailureCount";

                        long failureCount = await this._counterStore.IncrementAndGetAsync(failureCountCacheKey, 1, ttl, 1, request);

                        if (failureCount > circuitBreakerMethod.PermitLimit)
                        {
                            // If failure limit is exceeded, placing this limit into Trial mode, which limits request rate to 1 request per trial interval
                            CircuitBreakerRateLimitMethod.PutIntoTrial(uniqueCacheKey);
                        }
                    }
                    else
                    {
                        // Just lifting the trial mode
                        CircuitBreakerRateLimitMethod.ReleaseFromTrial(uniqueCacheKey);
                    }
                }
                catch (Exception ex)
                {
                    this._log(LogLevel.Error, $"ThrottlingTroll failed. {ex}");

                    if (limit.ShouldThrowOnFailures)
                    {
                        throw;
                    }
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

        /// <summary>
        /// Caching the current config value, so that we don't need to re-apply global settings every time
        /// </summary>
        private ThrottlingTrollConfig _currentConfig;
    }
}