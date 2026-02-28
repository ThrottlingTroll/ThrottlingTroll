using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ThrottlingTroll.CounterStores.EfCore
{
    /// <summary>
    /// Implements Store for rate limit counters using Entity Framework Core
    /// </summary>
    public class EfCoreCounterStore : ICounterStore
    {
        /// <summary>
        /// Ctor
        /// </summary>
        public EfCoreCounterStore(Action<DbContextOptionsBuilder> efConfigFunc, EfCoreCounterStoreSettings settings = null)
        {
            this._configFunc = efConfigFunc;
            this._settings = settings ?? new EfCoreCounterStoreSettings();
        }

        /// <inheritdoc />
        public Action<LogLevel, string> Log { get; set; }

        /// <inheritdoc />
        public async Task<long> GetAsync(string key, IHttpRequestProxy request)
        {
            var retryCount = this._settings.MaxAttempts - 2;
            while (true)
            {
                try
                {
                    using var db = new CounterDbContext(this._configFunc);

                    var counter = db.ThrottlingTrollCounters
                        .Where(c => c.Id == key && c.ExpiresAt >= DateTimeOffset.UtcNow)
                        .AsNoTracking()
                        .SingleOrDefault()
                    ;

                    return counter == null ? 0 : counter.Count;
                }
                catch (Exception ex)
                {
                    if (retryCount < 0)
                    {
                        throw;
                    }

                    this.LogWarning($"ThrottlingTroll transiently failed to get a counter. {ex}");
                }

                await Task.Delay(Random.Shared.Next(this._settings.MaxDelayBetweenAttemptsInMilliseconds));

                retryCount--;
            }
        }

        /// <inheritdoc />
        public async Task<long> IncrementAndGetAsync(string key, long cost, long ttlInTicks, CounterStoreIncrementAndGetOptions options, long maxCounterValueToSetTtl, IHttpRequestProxy request)
        {
            this.RunCleanupIfNeeded();

            var ttl = new DateTimeOffset(ttlInTicks, TimeSpan.Zero);

            var retryCount = this._settings.MaxAttempts - 2;
            while (true)
            {
                try
                {
                    using var db = new CounterDbContext(this._configFunc);

                    // Need REPEATABLE READ level, so that we read our own writes
                    using var tx = db.Database.BeginTransaction(System.Data.IsolationLevel.RepeatableRead);

                    // Atomically incrementing with a raw SQL
                    int rowsAffected = await db.Database.ExecuteSqlRawAsync(
                        $"UPDATE {this.GetTableName(db)} SET Count = Count + {cost} WHERE Id = '{key}'"
                    );

                    if (rowsAffected > 1)
                    {
                        // Failing quickly
                        retryCount = -1;
                        throw new InvalidOperationException("Looks like your table is missing the PRIMARY KEY constraint");
                    }
                    else if (rowsAffected < 1)
                    {
                        // The counter doesn't exist - creating it

                        db.ThrottlingTrollCounters.Add(new ThrottlingTrollCounter
                        {
                            Id = key,
                            Count = cost,
                            ExpiresAt = options == CounterStoreIncrementAndGetOptions.IncrementTtl ?
                                DateTimeOffset.UtcNow + TimeSpan.FromTicks(ttlInTicks) :
                                new DateTimeOffset(ttlInTicks, TimeSpan.Zero)
                        });

                        await db.SaveChangesAsync();
                        await tx.CommitAsync();

                        return cost;
                    }

                    // Reading the new value
                    var counter = db.ThrottlingTrollCounters.Single(c => c.Id == key);

                    // If expired - resetting the count
                    if (counter.ExpiresAt < DateTimeOffset.UtcNow)
                    {
                        counter.Count = 1;
                    }

                    // Also updating TTL, if needed
                    if (counter.Count <= maxCounterValueToSetTtl)
                    {
                        counter.ExpiresAt = options == CounterStoreIncrementAndGetOptions.IncrementTtl ?
                            counter.ExpiresAt + TimeSpan.FromTicks(ttlInTicks) :
                            new DateTimeOffset(ttlInTicks, TimeSpan.Zero);

                        await db.SaveChangesAsync();
                    }

                    await tx.CommitAsync();

                    return counter.Count;
                }
                catch (Exception ex)
                {
                    if (retryCount < 0)
                    {
                        throw;
                    }

                    this.LogWarning($"ThrottlingTroll transiently failed to increment a counter. {ex}");
                }

                await Task.Delay(Random.Shared.Next(this._settings.MaxDelayBetweenAttemptsInMilliseconds));

                retryCount--;
            }
        }

        /// <inheritdoc />
        public async Task DecrementAsync(string key, long cost, IHttpRequestProxy request)
        {
            this.RunCleanupIfNeeded();

            var retryCount = this._settings.MaxAttempts - 2;
            while (true)
            {
                try
                {
                    using var db = new CounterDbContext(this._configFunc);

                    var now = DateTimeOffset.UtcNow;

                    // Atomically decrementing with a raw SQL
                    int rowsAffected = await db.Database.ExecuteSqlRawAsync(
                        $"UPDATE {this.GetTableName(db)} SET Count = Count - {cost} WHERE Id = '{key}' AND Count > 0"
                    );

                    return;
                }
                catch (Exception ex)
                {
                    if (retryCount < 0)
                    {
                        throw;
                    }

                    this.LogWarning($"ThrottlingTroll transiently failed to decrement a counter. {ex}");
                }

                await Task.Delay(Random.Shared.Next(this._settings.MaxDelayBetweenAttemptsInMilliseconds));

                retryCount--;
            }
        }

        private readonly Action<DbContextOptionsBuilder> _configFunc;
        private readonly EfCoreCounterStoreSettings _settings;

        private DateTimeOffset _lastCleanupRunTimestamp = DateTimeOffset.UtcNow.AddSeconds(-50);

        private string _tableName;

        /// <summary>
        /// Asynchronously cleans up expired records from table
        /// </summary>
        private void RunCleanupIfNeeded()
        {
            var now = DateTimeOffset.UtcNow;
            var moment = now - TimeSpan.FromMinutes(this._settings.RunCleanupEveryMinutes);

            // Doing cleanup every minute
            if (this._lastCleanupRunTimestamp > moment)
            {
                return;
            }

            this._lastCleanupRunTimestamp = now;

            // Running asynchronously
            Task.Run(async () =>
            {
                var retryCount = this._settings.MaxAttempts - 2;
                while (true)
                {
                    try
                    {
                        using var db = new CounterDbContext(this._configFunc);

                        // Need to do this as part of a transaction
                        using var tx = db.Database.BeginTransaction(System.Data.IsolationLevel.RepeatableRead);

                        // Doing this in two steps. First step locks records to be deleted, which also ensures that we're not deleting a record in the middle of another transaction 
                        int rowsAffected = await db.Database.ExecuteSqlRawAsync(
                            $"UPDATE {this.GetTableName(db)} SET ExpiresAt = '{DateTimeOffset.MinValue:O}' WHERE ExpiresAt < '{DateTimeOffset.UtcNow.AddMinutes(-this._settings.DeleteExpiredCountersAfterMinutes):O}'"
                        );

                        // This second step actually deletes expired counters
                        if (rowsAffected > 0) 
                        {
                            rowsAffected = await db.Database.ExecuteSqlRawAsync(
                                $"DELETE FROM {this.GetTableName(db)} WHERE ExpiresAt = '{DateTimeOffset.MinValue:O}'"
                            );
                        }

                        await tx.CommitAsync();

                        return;
                    }
                    catch (Exception ex)
                    {
                        if (retryCount < 0)
                        {
                            this.LogWarning($"ThrottlingTroll failed to cleanup the table. {ex}");
                            return;
                        }
                    }

                    await Task.Delay(Random.Shared.Next(this._settings.MaxDelayBetweenAttemptsInMilliseconds));

                    retryCount--;
                }
            });
        }

        private string GetTableName(CounterDbContext db)
        {
            if (!string.IsNullOrEmpty(this._tableName))
            {
                return this._tableName;
            }

            var entityType = db.Model.FindEntityType(typeof(ThrottlingTrollCounter));

            this._tableName = entityType == null ? "ThrottlingTrollCounters" : entityType.GetSchemaQualifiedTableName();

            return this._tableName;
        }

        private void LogWarning(string msg)
        {
            if (this.Log != null)
            {
                this.Log(LogLevel.Warning, msg);
            }
        }
    }
}
