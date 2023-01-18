using StackExchange.Redis;
using System;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// Implements Store for rate limit counters using Redis
    /// </summary>
    public class RedisCounterStore : ICounterStore
    {
        private IConnectionMultiplexer _redis;
        public RedisCounterStore(IConnectionMultiplexer redis)
        {
            this._redis = redis;
        }

        /// <inheritdoc />
        public async Task<long> GetAsync(string key)
        {
            var db = this._redis.GetDatabase();

            var val = await db.StringGetAsync(key);

            return (long)val;
        }

        /// <inheritdoc />
        public async Task<long> IncrementAndGetAsync(string key, DateTimeOffset ttl)
        {
            var db = this._redis.GetDatabase();

            var val = await db.StringIncrementAsync(key);

            await db.KeyExpireAsync(key, ttl.UtcDateTime, ExpireWhen.HasNoExpiry);

            return val;
        }
    }
}