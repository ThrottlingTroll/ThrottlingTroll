using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// Implements Store for rate limit counters with IDistributedCache
    /// </summary>
    public class DistributedCacheCounterStore : ICounterStore
    {
        /// <summary>
        /// In IDistributedCache there's no way to set TTL once.
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

            public CacheEntry(byte[] bytes)
            {
                this.Count = BitConverter.ToInt64(bytes, 0);
                var ticks = BitConverter.ToInt64(bytes, sizeof(long));
                this.ExpiresAt = new DateTimeOffset(ticks, TimeSpan.Zero);
            }

            public byte[] ToBytes()
            {
                return BitConverter.GetBytes(this.Count).Concat(BitConverter.GetBytes(this.ExpiresAt.Ticks)).ToArray();
            }
        }

        private IDistributedCache _cache;

        private SemaphoreSlim _asyncLock = new SemaphoreSlim(1, 1);

        public DistributedCacheCounterStore(IDistributedCache cache)
        {
            this._cache = cache;
        }

        /// <inheritdoc />
        public async Task<long> GetAsync(string key)
        {
            var bytes = await this._cache.GetAsync(key);

            if (bytes == null)
            {
                return 0;
            }

            return new CacheEntry(bytes).Count;
        }

        /// <inheritdoc />
        public async Task<long> IncrementAndGetAsync(string key, DateTimeOffset ttl)
        {
            // This is just a local lock, but it's the best we can do with IDistributedCache
            await this._asyncLock.WaitAsync();

            try
            {
                CacheEntry cacheEntry;

                var bytes = await this._cache.GetAsync(key);

                if (bytes == null)
                {
                    cacheEntry = new CacheEntry(0, ttl);
                }
                else
                {
                    cacheEntry = new CacheEntry(bytes);
                }

                cacheEntry.Count++;

                await this._cache.SetAsync(
                    key,
                    cacheEntry.ToBytes(),
                    new DistributedCacheEntryOptions { AbsoluteExpiration = cacheEntry.ExpiresAt }
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