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
            this._counterStore = counterStore;
            this._log = log ?? ((l, s) => { });
            this._counterStore.Log = this._log;

            this.InitGetConfigTask(getConfigFunc, intervalToReloadConfigInSeconds);
        }

        /// <summary>
        /// Marks this instance as disposed
        /// </summary>
        public void Dispose()
        {
            this._disposed = true;
        }

        /// <summary>
        /// Checks if limit of calls is exceeded for a given request.
        /// If exceeded, returns number of seconds to retry after and unique counter ID. Otherwise returns null.
        /// </summary>
        protected internal async Task<LimitExceededResult> IsExceededAsync(IHttpRequestProxy request, List<Func<Task>> cleanupRoutines)
        {
            bool shouldThrowOnExceptions = false;
            try
            {
                var config = this.ApplyGlobalExtractors(await this._getConfigTask);

                if (config.Rules == null)
                {
                    return null;
                }

                // First checking if request whitelisted
                if (config.WhiteList != null && config.WhiteList.Any(filter => filter.IsMatch(request)))
                {
                    this._log(LogLevel.Information, $"ThrottlingTroll whitelisted {request.Method} {request.UriWithoutQueryString}");
                    return null;
                }

                var dtStart = DateTimeOffset.UtcNow;
                LimitExceededResult result = null;

                // Still need to check all limits, so that all counters get updated
                foreach (var limit in config.Rules)
                {
                    // The limit method defines whether we should throw on our internal failures
                    shouldThrowOnExceptions = limit.LimitMethod.ShouldThrowOnFailures;

                    long requestCost = limit.GetCost(request);

                    var limitCheckResult = await limit.IsExceededAsync(request, requestCost, this._counterStore, config.UniqueName, this._log);

                    if (limitCheckResult == null)
                    {
                        // The request did not match this rule
                        continue;
                    }

                    if (!limitCheckResult.IsExceeded)
                    {
                        // Decrementing this counter at the end of request processing
                        cleanupRoutines.Add(() => limit.OnRequestProcessingFinished(this._counterStore, limitCheckResult.CounterId, requestCost, this._log));

                        // The request matched the rule, but the limit was not exceeded.
                        continue;
                    }

                    // Doing the delay logic
                    if (limit.MaxDelayInSeconds > 0)
                    {
                        bool awaitedSuccessfully = false;

                        while ((DateTimeOffset.UtcNow - dtStart).TotalSeconds <= limit.MaxDelayInSeconds)
                        {
                            if (!await limit.IsStillExceededAsync(this._counterStore, limitCheckResult.CounterId))
                            {
                                // Doing double-check
                                limitCheckResult = await limit.IsExceededAsync(request, requestCost, this._counterStore, config.UniqueName, this._log);

                                if (!limitCheckResult.IsExceeded)
                                {
                                    // Decrementing this counter at the end of request processing
                                    cleanupRoutines.Add(() => limit.OnRequestProcessingFinished(this._counterStore, limitCheckResult.CounterId, requestCost, this._log));

                                    awaitedSuccessfully = true;

                                    break;
                                }
                            }

                            await Task.Delay(this._sleepTimeSpan);
                        }

                        if (awaitedSuccessfully)
                        {
                            continue;
                        }
                    }

                    // this limit was exceeded
                    if
                    (
                        (result == null)
                        ||
                        (
                            int.TryParse(result.RetryAfterHeaderValue, out int first) &&
                            int.TryParse(limitCheckResult.RetryAfterHeaderValue, out int second) &&
                            first < second
                        )
                    )
                    {
                        // Will return result with biggest RetryAfterInSeconds
                        result = limitCheckResult;
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                this._log(LogLevel.Error, $"ThrottlingTroll failed. {ex}");

                if (shouldThrowOnExceptions)
                {
                    throw;
                }
            }

            return null;
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

        private ThrottlingTrollConfig ApplyGlobalExtractors(ThrottlingTrollConfig config)
        {
            if (config.Rules != null)
            {
                foreach (var rule in config.Rules)
                {
                    if (rule.IdentityIdExtractor == null)
                    {
                        rule.IdentityIdExtractor = this._identityIdExtractor;
                    }

                    if (rule.CostExtractor == null)
                    {
                        rule.CostExtractor = this._costExtractor;
                    }
                }
            }

            if (config.WhiteList != null)
            {
                foreach (var rule in config.WhiteList)
                {
                    if (rule.IdentityIdExtractor == null)
                    {
                        rule.IdentityIdExtractor = this._identityIdExtractor;
                    }
                }
            }

            return config;
        }
    }
}