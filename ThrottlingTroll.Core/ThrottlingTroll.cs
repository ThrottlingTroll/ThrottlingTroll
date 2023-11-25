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
        /// Marks this instance as disposed
        /// </summary>
        public void Dispose()
        {
            this._disposed = true;
        }

        /// <summary>
        /// Checks if ingress limit is exceeded for a given request.
        /// Also checks whether there're any <see cref="ThrottlingTrollTooManyRequestsException"/>s from egress.
        /// Returns a list of check results for rules that this request matched.
        /// </summary>
        protected async Task<List<LimitCheckResult>> IsIngressOrEgressExceededAsync(IHttpRequestProxy request, List<Func<Task>> cleanupRoutines, Func<List<LimitCheckResult>, Task> nextAction)
        {
            // First trying ingress
            var checkList = await this.IsExceededAsync(request, cleanupRoutines);

            if (checkList.Any(r => r.RequestsRemaining < 0))
            {
                return checkList;
            }

            // Also trying to propagate egress to ingress
            try
            {
                await nextAction(checkList);
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
            bool shouldThrowOnExceptions = false;
            try
            {
                var config = this.ApplyGlobalExtractors(await this._getConfigTask);

                if (config.Rules == null)
                {
                    return result;
                }

                // First checking if request whitelisted
                if (config.WhiteList != null && config.WhiteList.Any(filter => filter.IsMatch(request)))
                {
                    this._log(LogLevel.Information, $"ThrottlingTroll whitelisted {request.Method} {request.UriWithoutQueryString}");
                    return result;
                }

                var dtStart = DateTimeOffset.UtcNow;

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

                    if ((limitCheckResult.RequestsRemaining < 0) && limit.MaxDelayInSeconds > 0)
                    {
                        // Doing the delay logic
                        while ((DateTimeOffset.UtcNow - dtStart).TotalSeconds <= limit.MaxDelayInSeconds)
                        {
                            if (!await limit.IsStillExceededAsync(this._counterStore, limitCheckResult.CounterId))
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
                        cleanupRoutines.Add(() => limit.OnRequestProcessingFinished(this._counterStore, limitCheckResult.CounterId, requestCost, this._log));
                    }

                    result.Add(limitCheckResult);
                }
            }
            catch (Exception ex)
            {
                this._log(LogLevel.Error, $"ThrottlingTroll failed. {ex}");

                if (shouldThrowOnExceptions)
                {
                    throw;
                }
            }

            return result;
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