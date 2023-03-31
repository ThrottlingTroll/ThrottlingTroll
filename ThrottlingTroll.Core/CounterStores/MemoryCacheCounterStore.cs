using System;
using System.Runtime.Caching;
using System.Threading;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// Implements Store for rate limit counters with System.Runtime.Caching.MemoryCache
    /// </summary>
    public class MemoryCacheCounterStore : ICounterStore
    {
        /// <summary>
        /// In MemoryCache there's no way to set TTL once.
        /// So we have to remember each item's expiration time separately. 
        /// This is what this class is for.
        /// </summary>
        internal class CacheEntry
        {
            public long Count;
            public DateTimeOffset ExpiresAt;

            public CacheEntry(long count, DateTimeOffset expiresAt)
            {
                this.Count = count;
                this.ExpiresAt = expiresAt;
            }
        }

        private readonly MemoryCache _cache = MemoryCache.Default;

        private readonly SemaphoreSlim _asyncLock = new SemaphoreSlim(1, 1);

        /// <inheritdoc />
        public async Task<long> GetAsync(string key)
        {
            var entry = this._cache.Get(key) as CacheEntry;

            return entry == null ? 0 : entry.Count;
        }

        /// <inheritdoc />
        public async Task<long> IncrementAndGetAsync(string key, DateTimeOffset ttl)
        {
            // This is just a local lock, but it's the best we can do with IDistributedCache
            await this._asyncLock.WaitAsync();

            try
            {
                var cacheEntry = this._cache.Get(key) as CacheEntry;

                if (cacheEntry == null)
                {
                    cacheEntry = new CacheEntry(0, ttl);
                }

                cacheEntry.Count++;

                this._cache.Set
                (
                    key, 
                    cacheEntry, 
                    new CacheItemPolicy { AbsoluteExpiration = cacheEntry.ExpiresAt }
                );

                return cacheEntry.Count;
            }
            finally
            {
                this._asyncLock.Release();
            }
        }
    }
}