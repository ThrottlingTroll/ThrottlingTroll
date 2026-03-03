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
        public async Task<long> IncrementAndGetAsync(string key, long cost, CounterTtl ttl, IHttpRequestProxy request)
        {
            var db = this._redis.GetDatabase();

            switch (ttl)
            {
                case CounterAbsoluteTtl absTtl:
                {
                    // Need to also check if TTL was set at all (that's because some people reported that INCR might not be atomic)
                    var script = LuaScript.Prepare(
                        $"local c = redis.call('INCRBY', @key, @cost) if c <= tonumber(@maxCounterValueToSetTtl) or redis.call('PTTL', @key) < 0 then redis.call('PEXPIREAT', @key, @absTtlInMs) end return c"
                    );

                    var res = await db.ScriptEvaluateAsync(
                        script,
                        new
                        {
                            key = (RedisKey)key,
                            cost,
                            absTtlInMs = absTtl.Ttl.ToUnixTimeMilliseconds(),
                            maxCounterValueToSetTtl = absTtl.MaxCounterValueToSetTtl
                        });

                    return (long)res;
                }

                case CounterIncrementalTtl incTtl:
                {
                    // Also reading the current TTL (if any) and incrementing it
                    var script = LuaScript.Prepare(
                        $"local c = redis.call('INCRBY', @key, @cost) if c <= tonumber(@maxCounterValueToSetTtl) then local ttl = redis.call('PTTL', @key) if ttl < 0 then ttl = 0 end redis.call('PEXPIRE', @key, ttl + @incTtlInMs) end return c"
                    );

                    var res = await db.ScriptEvaluateAsync(
                        script,
                        new
                        {
                            key = (RedisKey)key,
                            cost,
                            incTtlInMs = incTtl.Ttl.TotalMilliseconds,
                            maxCounterValueToSetTtl = incTtl.MaxCounterValueToSetTtl
                        });

                    return (long)res;
                }
            }

            // Should never happen
            throw new NotSupportedException($"Unknown TTL type {ttl.GetType().Name}");
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
