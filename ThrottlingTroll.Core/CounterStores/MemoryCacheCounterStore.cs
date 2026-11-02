using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// Implements Store for rate limit counters with <see cref="IMemoryCache"/>
    /// </summary>
    public class MemoryCacheCounterStore : ICounterStore
    {
        /// <summary>
        /// In IMemoryCache there's no way to get an entry's TTL.
        /// So we have to remember each item's expiration time separately. 
        /// This is what this class is for.
        /// </summary>
        internal class CacheEntry
        {
            public long Count;
            public DateTimeOffset ExpiresAt;
        }

        /// <summary>
        /// Creates a counter store backed by a new in-memory cache.
        /// </summary>
        public MemoryCacheCounterStore()
            : this(new MemoryCache(new MemoryCacheOptions()))
        {
        }

        /// <summary>
        /// Creates a counter store backed by the specified in-memory cache.
        /// The passed <see cref="IMemoryCache"/> instance must not be size-limited.
        /// </summary>
        public MemoryCacheCounterStore(IMemoryCache cache)
        {
            if (cache == null)
            {
                throw new ArgumentNullException(nameof(cache));
            }

            this.ThrowIfCacheIsSizeLimited(cache);

            this._cache = cache;
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
        public async Task<long> IncrementAndGetAsync(string key, long cost, CounterTtl ttl, IHttpRequestProxy request)
        {
            await this._asyncLock.WaitAsync();

            try
            {
                var cacheEntry = this._cache.Get(key) as CacheEntry ?? new CacheEntry();

                // Incrementing the counter. For newly created CacheEntry it will be set to cost.
                cacheEntry.Count += cost;

                if (cacheEntry.Count <= ttl.MaxCounterValueToSetTtl)
                {
                    switch (ttl)
                    {
                        case CounterAbsoluteTtl absTtl:

                            cacheEntry.ExpiresAt = absTtl.Ttl;

                            break;

                        case CounterIncrementalTtl incTtl:

                            if (cacheEntry.ExpiresAt == default)
                            {
                                cacheEntry.ExpiresAt = DateTimeOffset.UtcNow;
                            }

                            cacheEntry.ExpiresAt += incTtl.Ttl;

                            break;
                    }
                }

                this._cache.Set(key, cacheEntry, cacheEntry.ExpiresAt);

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
                    this._cache.Set(key, cacheEntry, cacheEntry.ExpiresAt);
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

        private void ThrowIfCacheIsSizeLimited(IMemoryCache cache)
        {
            // Trying to create an entry with a value, but no size - just to see if it throws.
            var testKey = new object();
            try
            {
                using (var entry = cache.CreateEntry(testKey))
                {
                    entry.Value = testKey;
                }
            }
            catch (InvalidOperationException ex)
            {
                throw new ArgumentException("Size-limited caches are not supported.", nameof(cache), ex);
            }
            cache.Remove(testKey);
        }

        private readonly IMemoryCache _cache;

        private readonly SemaphoreSlim _asyncLock = new SemaphoreSlim(1, 1);
    }
}