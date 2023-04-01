using System;
using System.Runtime.Caching;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// Implements a Fixed Window throttling algorithm
    /// </summary>
    public class FixedWindowRateLimitMethod : RateLimitMethod
    {
        private readonly MemoryCache _cache = MemoryCache.Default;

        /// <summary>
        /// Window size in seconds
        /// </summary>
        public int IntervalInSeconds { get; set; }

        /// <inheritdoc />
        internal override async Task<int> IsExceededAsync(string limitKey, ICounterStore store)
        {
            if (this.IntervalInSeconds <= 0)
            {
                return 0;
            }

            // First checking our local memory cache for the "counter exceeded" flag
            string limitKeyExceededKey = $"{limitKey}-exceeded";

            if (this._cache.Get(limitKeyExceededKey) != null)
            {
                return this.IntervalInSeconds;
            }

            var now = DateTime.UtcNow;

            var ttl = now - TimeSpan.FromMilliseconds(now.Millisecond) + TimeSpan.FromSeconds(this.IntervalInSeconds);

            // Now checking the actual count
            long count = await store.IncrementAndGetAsync(limitKey, ttl);

            if (count > this.PermitLimit)
            {
                // Remember the fact that this counter exceeded in local cache
                this._cache.Set( limitKeyExceededKey, true, new CacheItemPolicy { AbsoluteExpiration = ttl } );

                return this.IntervalInSeconds;
            }
            else
            {
                return 0;
            }
        }
    }
}