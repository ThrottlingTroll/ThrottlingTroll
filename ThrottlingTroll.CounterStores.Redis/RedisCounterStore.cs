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

            RedisResult res;
            if (options == CounterStoreIncrementAndGetOptions.IncrementTtl)
            {
                // Also reading the current TTL (if any) and incrementing it
                var script = LuaScript.Prepare(
                    $"local c = redis.call('INCRBY', @key, @cost) if c <= tonumber(@maxCounterValueToSetTtl) then local ttl = redis.call('PTTL', @key) if ttl < 0 then ttl = 0 end redis.call('PEXPIRE', @key, ttl + @incTtlInMs) end return c"
                );

                res = await db.ScriptEvaluateAsync(
                    script,
                    new
                    {
                        key = (RedisKey)key,
                        cost,
                        incTtlInMs = ttlInTicks / TimeSpan.TicksPerMillisecond,
                        maxCounterValueToSetTtl
                    });
            }
            else
            {
                // Need to also check if TTL was set at all (that's because some people reported that INCR might not be atomic)
                var script = LuaScript.Prepare(
                    $"local c = redis.call('INCRBY', @key, @cost) if c <= tonumber(@maxCounterValueToSetTtl) or redis.call('PTTL', @key) < 0 then redis.call('PEXPIREAT', @key, @absTtlInMs) end return c"
                );

                var absTtl = new DateTimeOffset(ttlInTicks, TimeSpan.Zero);

                res = await db.ScriptEvaluateAsync(
                    script,
                    new
                    {
                        key = (RedisKey)key,
                        cost,
                        absTtlInMs = absTtl.ToUnixTimeMilliseconds(),
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
