using System;
using System.Collections.Generic;
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
        private readonly MemoryCache _cache = MemoryCache.Default;

        /// <summary>
        /// Window size in seconds
        /// </summary>
        public int IntervalInSeconds { get; set; }

        /// <summary>
        /// Number of intervals to divide the window into. Should not be less than <see cref="IntervalInSeconds"/>.
        /// </summary>
        public int NumOfBuckets { get; set; }

        /// <inheritdoc />
        public override async Task<int> IsExceededAsync(string limitKey, ICounterStore store)
        {
            if (this.IntervalInSeconds <= 0 || this.NumOfBuckets <= 0)
            {
                return 0;
            }

            int bucketSizeInSeconds = this.IntervalInSeconds / this.NumOfBuckets;

            if (bucketSizeInSeconds <= 0)
            {
                return 0;
            }

            var now = DateTime.UtcNow;

            int curBucketId = (now.Second / bucketSizeInSeconds) % this.NumOfBuckets;
            string curBucketKey = $"{limitKey}-{curBucketId}";

            // Will load contents of all buckets in parallel
            var tasks = new List<Task<long>>();

            var ttl = now - TimeSpan.FromMilliseconds(now.Millisecond) + TimeSpan.FromSeconds(bucketSizeInSeconds * this.NumOfBuckets);

            // Incrementing and getting the current bucket
            tasks.Add(store.IncrementAndGetAsync(curBucketKey, ttl));

            // Now checking our local memory cache for the "counter exceeded" flag.
            // Need to do that _after_ the current bucket gets incremented, since for a sliding window the correct count in each bucket matters.
            string limitKeyExceededKey = $"{limitKey}-exceeded";

            if (this._cache.Get(limitKeyExceededKey) != null)
            {
                // But in this case it's OK to abandon the increment task, so the optimization still takes place.
                return bucketSizeInSeconds;
            }

            // Also getting values from other buckets
            var otherBucketIds = Enumerable.Range(0, this.NumOfBuckets).Where(id => id != curBucketId);
            foreach (var bucketId in otherBucketIds)
            {
                tasks.Add(store.GetAsync($"{limitKey}-{bucketId}"));
            }

            // Aggregating all buckets
            long count = (await Task.WhenAll(tasks)).Sum();

            if (count > this.PermitLimit)
            {
                // Remember the fact that this counter exceeded in local cache
                var limitKeyExceededTtl = now - TimeSpan.FromMilliseconds(now.Millisecond) + TimeSpan.FromSeconds(bucketSizeInSeconds);
                this._cache.Set(limitKeyExceededKey, true, new CacheItemPolicy { AbsoluteExpiration = limitKeyExceededTtl });

                return bucketSizeInSeconds;
            }
            else
            {
                return 0;
            }
        }

        /// <inheritdoc />
        public override Task DecrementAsync(string limitKey, ICounterStore store)
        {
            // Doing nothing
            return Task.CompletedTask;
        }
    }
}