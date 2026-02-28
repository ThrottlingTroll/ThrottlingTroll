using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ThrottlingTroll.CounterStores.AzureTable
{
    /// <summary>
    /// Implements Store for rate limit counters using Azure Tables / Cosmos DB for Table
    /// </summary>
    public class AzureTableCounterStore : ICounterStore
    {
        /// <summary>
        /// Uses 'AzureWebJobsStorage' environment variable as a connection string to create TableServiceClient and TableCounterStore.
        /// </summary>
        /// <param name="tableName">Name of the table to store counters in. Table will be auto-created.</param>
        public AzureTableCounterStore(string tableName = "ThrottlingTroll")
        {
            var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentNullException("Couldn't find Storage connection string in 'AzureWebJobsStorage' environment variable");
            }

            var client = new TableServiceClient(connectionString);
            client.CreateTableIfNotExists(tableName);
            this._tableClient = client.GetTableClient(tableName);
        }

        /// <summary>
        /// Uses the given connection string to create TableServiceClient and TableCounterStore.
        /// </summary>
        /// <param name="connectionString">Azure Storage connection string to use</param>
        /// <param name="tableName">Name of the table to store counters in. Table will be auto-created.</param>
        public AzureTableCounterStore(string connectionString, string tableName)
        {
            var client = new TableServiceClient(connectionString);
            client.CreateTableIfNotExists(tableName);
            this._tableClient = client.GetTableClient(tableName);
        }

        /// <summary>
        /// Takes a pre-configured TableServiceClient instance and creates TableCounterStore on top of it.
        /// </summary>
        /// <param name="client">TableServiceClient instance to use</param>
        /// <param name="tableName">Name of the table to store counters in. Table will be auto-created.</param>
        public AzureTableCounterStore(TableServiceClient client, string tableName = "ThrottlingTroll")
        {
            client.CreateTableIfNotExists(tableName);
            this._tableClient = client.GetTableClient(tableName);
        }

        /// <inheritdoc />
        public Action<LogLevel, string> Log { get; set; }

        /// <inheritdoc />
        public async Task<long> GetAsync(string key, IHttpRequestProxy request)
        {
            key = this.ConvertKey(key);

            var entity = await this._tableClient.GetEntityIfExistsAsync<CounterEntity>(key, key);

            if (!entity.HasValue || entity.Value.ExpiresAt < DateTimeOffset.UtcNow)
            {
                return 0;
            }

            return entity.Value.Count;
        }

        /// <inheritdoc />
        public async Task<long> IncrementAndGetAsync(string key, long cost, long ttlInTicks, CounterStoreIncrementAndGetOptions options, long maxCounterValueToSetTtl, IHttpRequestProxy request)
        {
            this.RunCleanupIfNeeded();

            key = this.ConvertKey(key);

            var retryCount = MaxRetries - 2;
            while (true)
            {
                try
                {
                    var entity = await this._tableClient.GetEntityIfExistsAsync<CounterEntity>(key, key);

                    // Calculating the new TTL value
                    DateTimeOffset newTtl;
                    if (options == CounterStoreIncrementAndGetOptions.IncrementTtl)
                    {
                        newTtl = DateTimeOffset.UtcNow;

                        if (entity.Value?.ExpiresAt > newTtl)
                        {
                            newTtl = entity.Value.ExpiresAt;
                        }

                        newTtl += TimeSpan.FromTicks(ttlInTicks);
                    }
                    else
                    {
                        newTtl = new DateTimeOffset(ttlInTicks, TimeSpan.Zero);
                    }

                    if (!entity.HasValue)
                    {
                        // Just adding a new record - and that's it
                        var res = await this._tableClient.AddEntityAsync(new CounterEntity { PartitionKey = key, RowKey = key, Count = cost, ExpiresAt = newTtl });

                        return cost;
                    }

                    var counter = entity.Value;

                    if (counter.ExpiresAt < DateTimeOffset.UtcNow || counter.Count < 1)
                    {
                        // Recreating the entity
                        counter.Count = cost;
                        counter.ExpiresAt = newTtl;
                    }
                    else
                    {
                        // Incrementing the counter
                        counter.Count += cost;

                        if (counter.Count <= maxCounterValueToSetTtl)
                        {
                            counter.ExpiresAt = newTtl;
                        }
                    }

                    await this._tableClient.UpdateEntityAsync(counter, counter.ETag);

                    return counter.Count;
                }
                catch (RequestFailedException ex)
                {
                    if (retryCount < 0)
                    {
                        throw;
                    }

                    if (ex.Status != (int)HttpStatusCode.Conflict && ex.Status != (int)HttpStatusCode.PreconditionFailed)
                    {
                        throw;
                    }
                }

                await Task.Delay(Random.Shared.Next(MaxDelayBetweenRetriesInMs));

                retryCount--;
            }
        }

        /// <inheritdoc />
        public async Task DecrementAsync(string key, long cost, IHttpRequestProxy request)
        {
            this.RunCleanupIfNeeded();

            key = this.ConvertKey(key);

            var retryCount = MaxRetries - 2;
            while (true)
            {
                try
                {
                    var entity = await this._tableClient.GetEntityIfExistsAsync<CounterEntity>(key, key);

                    if (!entity.HasValue)
                    {
                        return;
                    }

                    var counter = entity.Value;

                    if (counter.ExpiresAt < DateTimeOffset.UtcNow || counter.Count < 1)
                    {
                        return;
                    }

                    counter.Count -= cost;

                    await this._tableClient.UpdateEntityAsync(counter, counter.ETag);

                    return;
                }
                catch (RequestFailedException ex)
                {
                    if (retryCount < 0)
                    {
                        throw;
                    }

                    if (ex.Status != (int)HttpStatusCode.PreconditionFailed)
                    {
                        throw;
                    }
                }

                await Task.Delay(Random.Shared.Next(MaxDelayBetweenRetriesInMs));

                retryCount--;
            }
        }

        internal class CounterEntity : ITableEntity
        {
            public string PartitionKey { get; set; } = default!;

            public string RowKey { get; set; } = default!;

            public DateTimeOffset? Timestamp { get; set; } = default!;

            public ETag ETag { get; set; } = default!;

            public long Count { get; set; }

            public DateTimeOffset ExpiresAt { get; set; }
        }

        private const int MaxRetries = 10;
        private const int MaxDelayBetweenRetriesInMs = 100;

        private DateTimeOffset _lastCleanupRunTimestamp = DateTimeOffset.MinValue;

        private readonly TableClient _tableClient;

        private static readonly Regex KeyConversionRegex = new Regex("[\\\\/#?]", RegexOptions.Compiled);

        /// <summary>
        /// Removes symbols prohibited in PartitionKey/RowKey from cache key string
        /// </summary>
        private string ConvertKey(string key)
        {
            return KeyConversionRegex.Replace(key, "-");
        }

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
                    // All items that expired 10 minutes ago
                    var items = this._tableClient.QueryAsync<CounterEntity>(r => r.ExpiresAt < moment);

                    await foreach (var item in items)
                    {
                        try
                        {
                            await this._tableClient.DeleteEntityAsync(item.PartitionKey, item.RowKey, item.ETag);
                        }
                        catch (RequestFailedException ex)
                        {
                            if (ex.Status != (int)HttpStatusCode.PreconditionFailed)
                            {
                                throw;
                            }
                        }
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