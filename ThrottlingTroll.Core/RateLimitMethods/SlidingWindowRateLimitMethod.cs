using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Caching;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// Implements a Sliding Window throttling algorithm
    /// </summary>
    public class SlidingWindowRateLimitMethod : RateLimitMethod
    {
        /// <summary>
        /// Window size in seconds
        /// </summary>
        public int IntervalInSeconds { get; set; }

        /// <summary>
        /// Number of intervals to divide the window into. Should not be less than <see cref="IntervalInSeconds"/>.
        /// </summary>
        public int NumOfBuckets { get; set; }

        /// <inheritdoc />
        public override int RetryAfterInSeconds => this.NumOfBuckets == 0 ? 0 : this.IntervalInSeconds / this.NumOfBuckets;

        /// <inheritdoc />
        public override async Task<int> IsExceededAsync(string limitKey, long cost, ICounterStore store, IHttpRequestProxy request)
        {
            if (this.IntervalInSeconds <= 0 || this.NumOfBuckets <= 0)
            {
                return int.MaxValue;
            }

            int bucketSizeInSeconds = this.IntervalInSeconds / this.NumOfBuckets;

            if (bucketSizeInSeconds <= 0)
            {
                return int.MaxValue;
            }

            var now = DateTimeOffset.UtcNow;

            int curBucketId = (now.Second / bucketSizeInSeconds) % this.NumOfBuckets;
            string curBucketKey = $"{limitKey}-{curBucketId}";

            // Will load contents of all buckets in parallel
            var tasks = new List<Task<long>>();

            var ttl = now - TimeSpan.FromMilliseconds(now.Millisecond) + TimeSpan.FromSeconds(bucketSizeInSeconds * this.NumOfBuckets);

            // Incrementing and getting the current bucket
            tasks.Add(
                store.IncrementAndGetAsync(
                    curBucketKey,
                    cost,
                    ttl.UtcTicks,
                    CounterStoreIncrementAndGetOptions.SetAbsoluteTtl,
                    cost,
                    request));

            // Now checking our local memory cache for the "counter exceeded" flag.
            // Need to do that _after_ the current bucket gets incremented, since for a sliding window the correct count in each bucket matters.
            string limitKeyExceededKey = $"{limitKey}-exceeded";

            if (this._cache.Get(limitKeyExceededKey) != null)
            {
                // But in this case it's OK to abandon the increment task, so the optimization still takes place.
                return -1;
            }

            // Also getting values from other buckets
            var otherBucketIds = Enumerable.Range(0, this.NumOfBuckets).Where(id => id != curBucketId);
            foreach (var bucketId in otherBucketIds)
            {
                tasks.Add(store.GetAsync($"{limitKey}-{bucketId}", request));
            }

            // Aggregating all buckets
            long count = (await Task.WhenAll(tasks)).Sum();

            if (count > this.PermitLimit)
            {
                // Remember the fact that this counter exceeded in local cache
                var limitKeyExceededTtl = now - TimeSpan.FromMilliseconds(now.Millisecond) + TimeSpan.FromSeconds(bucketSizeInSeconds);
                this._cache.Set(limitKeyExceededKey, true, new CacheItemPolicy { AbsoluteExpiration = limitKeyExceededTtl });

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
            if (this.IntervalInSeconds <= 0 || this.NumOfBuckets <= 0)
            {
                return false;
            }

            // Will load contents of all buckets in parallel
            var tasks = Enumerable.Range(0, this.NumOfBuckets).Select(bucketId => store.GetAsync($"{limitKey}-{bucketId}", request));

            // Aggregating all buckets
            long count = (await Task.WhenAll(tasks)).Sum();

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
            return $"{nameof(SlidingWindowRateLimitMethod)}({this.PermitLimit},{this.IntervalInSeconds},{this.NumOfBuckets})";
        }

        private readonly MemoryCache _cache = MemoryCache.Default;

        #region Telemetry
        internal override void AddTagsToActivity(Activity activity)
        {
            activity?.AddTag($"{nameof(SlidingWindowRateLimitMethod)}.{nameof(this.PermitLimit)}", this.PermitLimit);
            activity?.AddTag($"{nameof(SlidingWindowRateLimitMethod)}.{nameof(this.ShouldThrowOnFailures)}", base.ShouldThrowOnFailures);
            activity?.AddTag($"{nameof(SlidingWindowRateLimitMethod)}.{nameof(this.IgnoreAllowList)}", this.IgnoreAllowList);
            activity?.AddTag($"{nameof(SlidingWindowRateLimitMethod)}.{nameof(this.RetryAfterInSeconds)}", this.RetryAfterInSeconds);

            activity?.AddTag($"{nameof(SlidingWindowRateLimitMethod)}.{nameof(this.IntervalInSeconds)}", this.IntervalInSeconds);
            activity?.AddTag($"{nameof(SlidingWindowRateLimitMethod)}.{nameof(this.NumOfBuckets)}", this.NumOfBuckets);
        }
        #endregion
    }
}