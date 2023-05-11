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
        private readonly IConnectionMultiplexer _redis;

        /// <summary>
        /// Ctor
        /// </summary>
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
        public async Task<long> IncrementAndGetAsync(string key, DateTimeOffset ttl, long maxCounterValueToSetTtl)
        {
            var db = this._redis.GetDatabase();

            // Doing this with one atomic LUA script
            // Need to also check if TTL was set at all (that's because some people reported that INCR might not be atomic)
            var script = LuaScript.Prepare(
                $"local c = redis.call('INCR', @key) if c <= tonumber(@maxCounterValueToSetTtl) or redis.call('PTTL', @key) < 0 then redis.call('PEXPIREAT', @key, @absTtlInMs) end return c"
            );

            var val = await db.ScriptEvaluateAsync(script, new { key = (RedisKey)key, absTtlInMs = ttl.ToUnixTimeMilliseconds(), maxCounterValueToSetTtl });

            return (long)val;
        }

        /// <inheritdoc />
        public async Task DecrementAsync(string key)
        {
            var db = this._redis.GetDatabase();

            // Atomically decrementing and removing if 0 or less
            var script = LuaScript.Prepare(
                $"if redis.call('DECR', @key) < 1 then redis.call('DEL', @key) end"
            );
            await db.ScriptEvaluateAsync(script, new { key = (RedisKey)key });
        }
    }
}