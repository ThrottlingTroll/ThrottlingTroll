using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ThrottlingTroll.CounterStores.DistributedCache
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

        /// <summary>
        /// Ctor
        /// </summary>
        public DistributedCacheCounterStore(IDistributedCache cache)
        {
            this._cache = cache;
        }

        /// <inheritdoc />
        public Action<LogLevel, string> Log { get; set; }

        /// <inheritdoc />
        public async Task<long> GetAsync(string key, IHttpRequestProxy request)
        {
            var bytes = await this._cache.GetAsync(key);

            if (bytes == null)
            {
                return 0;
            }

            return new CacheEntry(bytes).Count;
        }

        /// <inheritdoc />
        public async Task<long> IncrementAndGetAsync(string key, long cost, long ttlInTicks, CounterStoreIncrementAndGetOptions options, long maxCounterValueToSetTtl, IHttpRequestProxy request)
        {
            // This is just a local lock, but it's the best we can do with IDistributedCache
            await this._asyncLock.WaitAsync();

            try
            {
                var bytes = await this._cache.GetAsync(key);

                CacheEntry cacheEntry;

                if (bytes == null)
                {
                    cacheEntry = new CacheEntry(
                        0,
                        options == CounterStoreIncrementAndGetOptions.IncrementTtl ?
                            DateTimeOffset.UtcNow + TimeSpan.FromTicks(ttlInTicks) :
                            new DateTimeOffset(ttlInTicks, TimeSpan.Zero));
                }
                else
                {
                    cacheEntry = new CacheEntry(bytes);
                }

                cacheEntry.Count += cost;

                if (cacheEntry.Count <= maxCounterValueToSetTtl)
                {
                    cacheEntry.ExpiresAt = options == CounterStoreIncrementAndGetOptions.IncrementTtl ?
                        cacheEntry.ExpiresAt + TimeSpan.FromTicks(ttlInTicks) :
                        new DateTimeOffset(ttlInTicks, TimeSpan.Zero);
                }

                await this.SetAsync(key, cacheEntry);

                return cacheEntry.Count;
            }
            finally
            {
                this._asyncLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task DecrementAsync(string key, long cost, IHttpRequestProxy request)
        {
            // This is just a local lock, but it's the best we can do with IDistributedCache
            await this._asyncLock.WaitAsync();

            try
            {
                CacheEntry cacheEntry;

                var bytes = await this._cache.GetAsync(key);

                if (bytes == null)
                {
                    return;
                }

                cacheEntry = new CacheEntry(bytes);

                cacheEntry.Count -= cost;

                if (cacheEntry.Count > 0)
                {
                    await this.SetAsync(key, cacheEntry);
                }
                else
                {
                    await this._cache.RemoveAsync(key);
                }
            }
            finally
            {
                this._asyncLock.Release();
            }
        }

        private IDistributedCache _cache;

        private SemaphoreSlim _asyncLock = new SemaphoreSlim(1, 1);

        private async Task SetAsync(string key, CacheEntry cacheEntry)
        {
            try
            {
                await this._cache.SetAsync(
                    key,
                    cacheEntry.ToBytes(),
                    new DistributedCacheEntryOptions { AbsoluteExpiration = cacheEntry.ExpiresAt }
                );
            }
            catch (ArgumentOutOfRangeException)
            {
                // This means "The absolute expiration value must be in the future". The solution here is to just drop this counter from cache

                await this._cache.RemoveAsync(key);
            }
        }
    }
}