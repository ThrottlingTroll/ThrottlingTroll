using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System;
using System.Threading.Tasks;

namespace ThrottlingTroll.CounterStores.Redis
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
        public Action<LogLevel, string> Log { get; set; }

        /// <inheritdoc />
        public async Task<long> GetAsync(string key, IHttpRequestProxy request)
        {
            var db = this._redis.GetDatabase();

            var val = await db.StringGetAsync(key);

            return (long)val;
        }

        /// <inheritdoc />
        public async Task<long> IncrementAndGetAsync(string key, long cost, long ttlInTicks, CounterStoreIncrementAndGetOptions options, long maxCounterValueToSetTtl, IHttpRequestProxy request)
        {
            var db = this._redis.GetDatabase();

            // Doing this with one atomic LUA script
            // Need to also check if TTL was set at all (that's because some people reported that INCR might not be atomic)

            RedisResult res;
            if (options == CounterStoreIncrementAndGetOptions.IncrementTtl)
            {
                var script = LuaScript.Prepare(
                    $"local c = redis.call('INCRBY', @key, @cost) local ttl = redis.call('PTTL', @key) if ttl < 0 then redis.call('PEXPIREAT', @key, @absTtlInMs) elseif c <= tonumber(@maxCounterValueToSetTtl) then redis.call('PEXPIREAT', @key, ttl + @incTtlInMs) end return c"
                );

                res = await db.ScriptEvaluateAsync(
                    script,
                    new
                    {
                        key = (RedisKey)key,
                        cost,
                        absTtlInMs = (DateTimeOffset.UtcNow.Ticks + ttlInTicks) / TimeSpan.TicksPerMillisecond,
                        incTtlInMs = ttlInTicks / TimeSpan.TicksPerMillisecond,
                        maxCounterValueToSetTtl
                    });
            }
            else
            {
                var script = LuaScript.Prepare(
                    $"local c = redis.call('INCRBY', @key, @cost) if c <= tonumber(@maxCounterValueToSetTtl) or redis.call('PTTL', @key) < 0 then redis.call('PEXPIREAT', @key, @absTtlInMs) end return c"
                );

                res = await db.ScriptEvaluateAsync(
                    script,
                    new
                    {
                        key = (RedisKey)key,
                        cost,
                        absTtlInMs = ttlInTicks / TimeSpan.TicksPerMillisecond,
                        maxCounterValueToSetTtl
                    });
            }

            return (long)res;
        }

        /// <inheritdoc />
        public async Task DecrementAsync(string key, long cost, IHttpRequestProxy request)
        {
            var db = this._redis.GetDatabase();

            // Atomically decrementing and removing if 0 or less
            var script = LuaScript.Prepare(
                $"if redis.call('DECRBY', @key, @cost) < 1 then redis.call('DEL', @key) end"
            );
            
            await db.ScriptEvaluateAsync(script, new { key = (RedisKey)key, cost });
        }
    }
}
