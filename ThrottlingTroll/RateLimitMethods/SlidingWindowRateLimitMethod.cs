using System;
using System.Collections.Generic;
using System.Linq;
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
        internal override async Task<int> IsExceededAsync(string limitKey, ICounterStore store)
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

            // Will load contents of all buckets in parallel
            var tasks = new List<Task<long>>();

            int curBucketId = (DateTime.UtcNow.Second / bucketSizeInSeconds) % this.NumOfBuckets;
            string curBucketKey = $"{limitKey}-{curBucketId}";

            var ttl = DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(DateTimeOffset.UtcNow.Millisecond) + TimeSpan.FromSeconds(bucketSizeInSeconds * this.NumOfBuckets);

            // Incrementing and getting the current bucket
            tasks.Add(store.IncrementAndGetAsync(curBucketKey, ttl));

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
                return bucketSizeInSeconds;
            }
            else
            {
                return 0;
            }
        }
    }
}