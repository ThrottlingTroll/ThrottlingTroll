using Microsoft.Extensions.Logging;
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

        /// <inheritdoc />
        public Action<LogLevel, string> Log { get; set; }

        /// <inheritdoc />
        public Task<long> GetAsync(string key, IHttpRequestProxy request)
        {
            var entry = this._cache.Get(key) as CacheEntry;

            return Task.FromResult(entry == null ? 0 : entry.Count);
        }

        /// <inheritdoc />
        public async Task<long> IncrementAndGetAsync(string key, long cost, long ttlInTicks, CounterStoreIncrementAndGetOptions options, long maxCounterValueToSetTtl, IHttpRequestProxy request)
        {
            await this._asyncLock.WaitAsync();

            try
            {
                var cacheEntry = this._cache.Get(key) as CacheEntry;

                var expiresAt = cacheEntry?.ExpiresAt ?? DateTimeOffset.UtcNow;
                var newTtl = options == CounterStoreIncrementAndGetOptions.IncrementTtl ?
                        expiresAt + TimeSpan.FromTicks(ttlInTicks) :
                        new DateTimeOffset(ttlInTicks, TimeSpan.Zero);

                if (cacheEntry == null)
                {
                    cacheEntry = new CacheEntry(0, newTtl);
                }

                cacheEntry.Count += cost;

                if (cacheEntry.Count <= maxCounterValueToSetTtl)
                {
                    cacheEntry.ExpiresAt = newTtl;
                }

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

        /// <inheritdoc />
        public async Task DecrementAsync(string key, long cost, IHttpRequestProxy request)
        {
            await this._asyncLock.WaitAsync();

            try
            {
                var cacheEntry = this._cache.Get(key) as CacheEntry;

                if (cacheEntry == null)
                {
                    return;
                }

                cacheEntry.Count -= cost;

                if (cacheEntry.Count > 0)
                {
                    this._cache.Set
                    (
                        key,
                        cacheEntry,
                        new CacheItemPolicy { AbsoluteExpiration = cacheEntry.ExpiresAt }
                    );
                }
                else
                {
                    this._cache.Remove(key);
                }
            }
            finally
            {
                this._asyncLock.Release();
            }
        }

        private readonly MemoryCache _cache = MemoryCache.Default;

        private readonly SemaphoreSlim _asyncLock = new SemaphoreSlim(1, 1);
    }
}