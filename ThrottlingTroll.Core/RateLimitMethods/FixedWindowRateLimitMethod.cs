using System;
using System.Diagnostics;
using System.Runtime.Caching;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// Implements a Fixed Window throttling algorithm
    /// </summary>
    public class FixedWindowRateLimitMethod : RateLimitMethod
    {
        /// <summary>
        /// Window size in seconds
        /// </summary>
        public int IntervalInSeconds { get; set; }

        /// <inheritdoc />
        public override int RetryAfterInSeconds => this.IntervalInSeconds;

        /// <inheritdoc />
        public override async Task<int> IsExceededAsync(string limitKey, long cost, ICounterStore store, IHttpRequestProxy request)
        {
            if (this.IntervalInSeconds <= 0)
            {
                return int.MaxValue;
            }

            // First checking our local memory cache for the "counter exceeded" flag
            string limitKeyExceededKey = $"{limitKey}-exceeded";

            if (this._cache.Get(limitKeyExceededKey) != null)
            {
                return -1;
            }

            var now = DateTimeOffset.UtcNow;

            var ttl = now - TimeSpan.FromMilliseconds(now.Millisecond) + TimeSpan.FromSeconds(this.IntervalInSeconds);

            // Now checking the actual count
            long count = await store.IncrementAndGetAsync(
                limitKey,
                cost,
                ttl.UtcTicks,
                CounterStoreIncrementAndGetOptions.SetAbsoluteTtl,
                cost,
                request);

            if (count > this.PermitLimit)
            {
                // Remember the fact that this counter exceeded in local cache
                this._cache.Set( limitKeyExceededKey, true, new CacheItemPolicy { AbsoluteExpiration = ttl } );

                return -1;
            }
            else
            {
                return this.PermitLimit - (int)count;
            }
        }

        /// <inheritdoc />
        public override async Task<bool> IsStillExceededAsync(string limitKey, ICounterStore store, IHttpRequestProxy request)
        {
            if (this.IntervalInSeconds <= 0)
            {
                return false;
            }

            long count = await store.GetAsync(limitKey, request);

            return count >= this.PermitLimit;
        }

        /// <inheritdoc />
        public override Task DecrementAsync(string limitKey, long cost, ICounterStore store, IHttpRequestProxy request)
        {
            // Doing nothing
            return Task.CompletedTask;
        }
        
        /// <inheritdoc/>
        public override string GetCacheKey()
        {
            return $"{nameof(FixedWindowRateLimitMethod)}({this.PermitLimit},{this.IntervalInSeconds})";
        }

        private readonly MemoryCache _cache = MemoryCache.Default;

        #region Telemetry
        internal override void AddTagsToActivity(Activity activity)
        {
            activity?.AddTag($"{nameof(FixedWindowRateLimitMethod)}.{nameof(this.PermitLimit)}", this.PermitLimit);
            activity?.AddTag($"{nameof(FixedWindowRateLimitMethod)}.{nameof(this.ShouldThrowOnFailures)}", base.ShouldThrowOnFailures);
            activity?.AddTag($"{nameof(FixedWindowRateLimitMethod)}.{nameof(this.IgnoreAllowList)}", this.IgnoreAllowList);
            activity?.AddTag($"{nameof(FixedWindowRateLimitMethod)}.{nameof(this.RetryAfterInSeconds)}", this.RetryAfterInSeconds);

            activity?.AddTag($"{nameof(FixedWindowRateLimitMethod)}.{nameof(this.IntervalInSeconds)}", this.IntervalInSeconds);
        }
        #endregion
    }
}