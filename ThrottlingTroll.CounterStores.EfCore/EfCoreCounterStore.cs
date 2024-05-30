using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ThrottlingTroll.CounterStores.EfCore
{
    /// <summary>
    /// Implements Store for rate limit counters using Redis
    /// </summary>
    public class EfCoreCounterStore : ICounterStore
    {
        /// <summary>
        /// Ctor
        /// </summary>
        public EfCoreCounterStore(Action<DbContextOptionsBuilder> configFunc)
        {
            this._configFunc = configFunc;
        }

        /// <inheritdoc />
        public Action<LogLevel, string> Log { get; set; }

        /// <inheritdoc />
        public Task<long> GetAsync(string key, IHttpRequestProxy request)
        {
            using var db = new CounterDbContext(this._configFunc);

            var counter = db.ThrottlingTrollCounters
                .Where(c => c.Id == key && c.ExpiresAt >= DateTimeOffset.UtcNow)
                .AsNoTracking()
                .SingleOrDefault()
            ;

            return Task.FromResult(counter == null ? 0 : counter.Count);
        }

        /// <inheritdoc />
        public async Task<long> IncrementAndGetAsync(string key, long cost, DateTimeOffset ttl, long maxCounterValueToSetTtl, IHttpRequestProxy request)
        {
            this.RunCleanupIfNeeded();

            using var db = new CounterDbContext(this._configFunc);
            // Need REPEATABLE READ level, so that the shared lock persists until the transaction finishes
            using var tx = db.Database.BeginTransaction(System.Data.IsolationLevel.RepeatableRead);

            // This puts a shared lock on the counter record
            var counter = db.ThrottlingTrollCounters.SingleOrDefault(c => c.Id == key);

            if (counter == null) 
            {
                // Just adding a new record - and that's it

                db.ThrottlingTrollCounters.Add(new ThrottlingTrollCounter
                {
                    Id = key,
                    Count = cost,
                    ExpiresAt = ttl
                });

                try
                {
                    await db.SaveChangesAsync();
                    await tx.CommitAsync();

                    return cost;
                }
                // TODO: only catch the conflict exception
                catch (Exception ex) 
                {
                    counter = db.ThrottlingTrollCounters.SingleOrDefault(c => c.Id == key);
                }
            }

            if (counter.ExpiresAt < DateTimeOffset.UtcNow || counter.Count < 1)
            {
                // Recreating the entity
                counter.Count = cost;
                counter.ExpiresAt = ttl;
            }
            else
            {
                // Incrementing the counter
                counter.Count += cost;

                if (counter.Count <= maxCounterValueToSetTtl)
                {
                    counter.ExpiresAt = ttl;
                }
            }

            await db.SaveChangesAsync();
            await tx.CommitAsync();

            return counter.Count;
        }

        /// <inheritdoc />
        public async Task DecrementAsync(string key, long cost, IHttpRequestProxy request)
        {
            this.RunCleanupIfNeeded();

            using var db = new CounterDbContext(this._configFunc);
            // Need REPEATABLE READ level, so that the shared lock persists until the transaction finishes
            using var tx = db.Database.BeginTransaction(System.Data.IsolationLevel.RepeatableRead);

            // This puts a shared lock on the counter record
            var counter = db.ThrottlingTrollCounters.SingleOrDefault(c => c.Id == key && c.ExpiresAt >= DateTimeOffset.UtcNow && c.Count > 0);

            if (counter == null)
            {
                return;
            }

            counter.Count -= cost;

            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        private readonly Action<DbContextOptionsBuilder> _configFunc;
        private DateTimeOffset _lastCleanupRunTimestamp = DateTimeOffset.MinValue;
        private const int MaxCountersToRemoveAtOnce = 100;

        /// <summary>
        /// Asynchronously cleans up expired records from table
        /// </summary>
        private void RunCleanupIfNeeded()
        {
            var now = DateTimeOffset.UtcNow;
            var moment = now - TimeSpan.FromMinutes(10);

            // Doing cleanup every 10 minutes
            if (this._lastCleanupRunTimestamp > moment)
            {
                return;
            }

            this._lastCleanupRunTimestamp = now;

            // Running asynchronously
            Task.Run(async () =>
            {
                try
                {
                    while(true)
                    {
                        using var db = new CounterDbContext(this._configFunc);

                        // Counters that expired 10 minutes ago
                        var expiredCounters = db.ThrottlingTrollCounters
                            .Where(c => c.ExpiresAt < moment)
                            .Take(MaxCountersToRemoveAtOnce)
                            .ToArray();

                        if (expiredCounters.Length <= 0)
                        {
                            break;
                        }

                        db.ThrottlingTrollCounters.RemoveRange(expiredCounters);

                        await db.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    if (this.Log != null)
                    {
                        this.Log(LogLevel.Warning, $"ThrottlingTroll failed to cleanup the table. {ex}");
                    }
                }
            });
        }
    }
}
