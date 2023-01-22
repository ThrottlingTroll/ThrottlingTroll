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

            /*
                KeyExpire() with ExpireWhen.HasNoExpiry was introduced in Redis 7.0, so it doesn't work in Azure Redis (which is <= 6.0) yet.

                Initializing the counter with 0 and some TTL _before_ doing StringIncrement() leads to a race:
                    if that TTL expires before StringIncrement() is called, StringIncrement() will make the key immortal.

                So the only option left is to _first_ set/increment the value and _then_ set its TTL with a conditional LUA script
            */

            var val = await db.StringIncrementAsync(key);

            var script = LuaScript.Prepare($"if redis.call('PTTL', @key) < 0 then redis.call('PEXPIREAT', @key, @absTtlInMs) end");
            db.ScriptEvaluate(script, new { key = (RedisKey)key, absTtlInMs = ttl.ToUnixTimeMilliseconds() });

            return val;
        }
    }
}