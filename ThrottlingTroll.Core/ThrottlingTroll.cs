using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// Generic ThrottlingTroll that can be used in any .NET application.
    /// </summary>
    public class ThrottlingTroll : ThrottlingTrollCore, IThrottlingTroll
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ThrottlingTroll"/> class.
        /// </summary>
        /// <param name="getConfigFunc"><see cref="ThrottlingTrollConfig"/> getter. Will be called every intervalToReloadConfigInSeconds.</param>
        /// <param name="intervalToReloadConfigInSeconds">When set to > 0, getConfigFunc will be called periodically with this interval. This allows you to dynamically change throttling rules and limits without restarting the service.</param>
        /// <param name="counterStore">An instance of <see cref="ICounterStore"/> to be used for storing counters.</param>
        /// <param name="identityIdExtractor">Identity ID extraction routine to be used for extracting Identity IDs from requests. null means all requests are coming from the same identity.</param>
        /// <param name="costExtractor">Request's cost extraction routine. The default cost (weight) of a request is 1, but this routine allows to override that. null means each request costs 1.</param>
        /// <param name="log">Logging routine.</param>
        public ThrottlingTroll
        (
            Func<Task<ThrottlingTrollConfig>> getConfigFunc,
            int intervalToReloadConfigInSeconds,
            ICounterStore counterStore,
            Func<IHttpRequestProxy, string> identityIdExtractor,
            Func<IHttpRequestProxy, long> costExtractor,
            Action<LogLevel, string> log
        )
            : base(log, counterStore, getConfigFunc, identityIdExtractor, costExtractor, intervalToReloadConfigInSeconds)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ThrottlingTroll"/> class.
        /// </summary>
        /// <param name="config">An instance of <see cref="ThrottlingTrollConfig"/> with rules and limits defined.</param>
        /// <param name="counterStore">An instance of <see cref="ICounterStore"/> to be used for storing counters.</param>
        /// <param name="identityIdExtractor">Identity ID extraction routine to be used for extracting Identity IDs from requests. null means all requests are coming from the same identity.</param>
        /// <param name="costExtractor">Request's cost extraction routine. The default cost (weight) of a request is 1, but this routine allows to override that. null means each request costs 1.</param>
        public ThrottlingTroll
        (
            ThrottlingTrollConfig config,
            ICounterStore counterStore,
            Func<IHttpRequestProxy, string> identityIdExtractor,
            Func<IHttpRequestProxy, long> costExtractor
        )
            : base((l, s) => { }, counterStore, () => Task.FromResult(config), identityIdExtractor, costExtractor, 0)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ThrottlingTroll"/> class.
        /// </summary>
        /// <param name="config">An instance of <see cref="ThrottlingTrollConfig"/> with rules and limits defined.</param>
        /// <param name="counterStore">An instance of <see cref="ICounterStore"/> to be used for storing counters.</param>
        public ThrottlingTroll(ThrottlingTrollConfig config, ICounterStore counterStore)
            : base((l, s) => { }, counterStore, () => Task.FromResult(config), null, null, 0)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ThrottlingTroll"/> class.
        /// </summary>
        /// <param name="config">Instance of <see cref="ThrottlingTrollConfig"/> with rules and limits defined.</param>
        public ThrottlingTroll(ThrottlingTrollConfig config)
            : base((l, s) => { }, new MemoryCacheCounterStore(), () => Task.FromResult(config), null, null, 0)
        {
        }

        /// <inheritdoc />
        public new Task<ThrottlingTrollConfig> GetCurrentConfig()
        {
            return base.GetCurrentConfig();
        }

        /// <inheritdoc />
        public Task<T> WithThrottlingTroll<T>(Func<ThrottlingTrollContext, Task<T>> todo, Func<ThrottlingTrollContext, Task<T>> onLimitExceeded, string methodName = null)
        {
            var requestProxy = new GenericRequestProxy { Method = methodName };

            return this.WithThrottlingTroll(requestProxy, todo, onLimitExceeded);
        }

        /// <inheritdoc />
        public async Task WithThrottlingTroll(Func<ThrottlingTrollContext, Task> todo, Func<ThrottlingTrollContext, Task> onLimitExceeded, string methodName = null)
        {
            await this.WithThrottlingTroll
            (
                async ctx =>
                {
                    await todo(ctx);
                    return 0;
                },
                async ctx =>
                {
                    await onLimitExceeded(ctx);
                    return 0;
                }
            );
        }

        /// <inheritdoc />
        public async Task<T> WithThrottlingTroll<T>(IHttpRequestProxy requestProxy, Func<ThrottlingTrollContext, Task<T>> todo, Func<ThrottlingTrollContext, Task<T>> onLimitExceeded)
        {
            var cleanupRoutines = new List<Func<Task>>();
            try
            {
                var checkList = await this.IsExceededAsync(requestProxy, cleanupRoutines);
                var ctx = new ThrottlingTrollContext { LimitCheckResults = checkList };

                if (checkList.Any(r => r.RequestsRemaining < 0))
                {
                    var result = await onLimitExceeded(ctx);

                    if (!ctx.ShouldContinueAsNormal)
                    {
                        return result;
                    }
                }

                try
                {
                    var result = await todo(ctx);

                    // Removing internal circuit breaking rules
                    await this.CheckAndBreakTheCircuit(requestProxy, null, null);

                    return result;
                }
                catch (Exception ex)
                {
                    // Adding internal circuit breaking rules
                    await this.CheckAndBreakTheCircuit(requestProxy, null, ex);

                    throw;
                }
            }
            finally
            {
                await Task.WhenAll(cleanupRoutines.Select(f => f()));
            }
        }

        /// <inheritdoc />
        public async Task WithThrottlingTroll(IHttpRequestProxy requestProxy, Func<ThrottlingTrollContext, Task> todo, Func<ThrottlingTrollContext, Task> onLimitExceeded)
        {
            await this.WithThrottlingTroll
            (
                requestProxy,
                async ctx =>
                {
                    await todo(ctx);
                    return 0;
                },
                async ctx =>
                {
                    await onLimitExceeded(ctx);
                    return 0;
                }
            );
        }
    }
}