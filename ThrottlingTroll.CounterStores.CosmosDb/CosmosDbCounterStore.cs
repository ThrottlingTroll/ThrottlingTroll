using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace ThrottlingTroll.CounterStores.CosmosDb
{
    /// <summary>
    /// Implements Store for rate limit counters using Cosmos DB and its Patch API
    /// </summary>
    public class CosmosDbCounterStore : ICounterStore
    {
        /// <summary>
        /// Ctor
        /// </summary>
        public CosmosDbCounterStore(CosmosClient cosmosClient, string dbName, string containerName = "ThrottlingTroll")
        {
            this._container = cosmosClient.GetContainer(dbName, containerName);
        }

        /// <inheritdoc />
        public Action<LogLevel, string> Log { get; set; }

        /// <inheritdoc />
        public async Task<long> GetAsync(string key, IHttpRequestProxy request)
        {
            try
            {
                var response = await this._container.ReadItemAsync<CounterEntity>(key, new PartitionKey(key));

                if (response.Resource.expiresAt < DateTime.UtcNow)
                {
                    return 0;
                }

                return response.Resource.count;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return 0;
            }
        }

        /// <inheritdoc />
        public async Task<long> IncrementAndGetAsync(string key, long cost, DateTimeOffset ttl, long maxCounterValueToSetTtl, IHttpRequestProxy request)
        {
            var now = DateTimeOffset.UtcNow;

            // Using this nonce value to ensure that either createTask or one of the other two succeed
            // (so that e.g. incrementTask does not get executed immediately after successful createTask due to a race condition)
            long nonce = Random.Shared.NextInt64();

            // Only one of these three tasks should succeed
            var createTask = this.CreateCounter(key, cost, ttl, nonce);
            var incrementTask = this.IncrementCounter(key, cost, ttl, maxCounterValueToSetTtl, now, nonce);
            var resetTask = this.ResetCounter(key, cost, ttl, now, nonce);

            // Awaiting all tasks (in parallel) without throwing
            await Task.WhenAny(createTask);
            await Task.WhenAny(incrementTask);
            await Task.WhenAny(resetTask);

            if (createTask.IsCompletedSuccessfully)
            {
                return createTask.Result;
            }
            if (incrementTask.IsCompletedSuccessfully)
            {
                return incrementTask.Result;
            }
            if (resetTask.IsCompletedSuccessfully)
            {
                return resetTask.Result;
            }

            // At this point all three tasks should fail (or at least, finish),
            // so just converting them into an AggregateException
            throw Task.WhenAll(createTask, incrementTask, resetTask).Exception!;
        }

        /// <inheritdoc />
        public async Task DecrementAsync(string key, long cost, IHttpRequestProxy request)
        {
            try
            {
                // Decrementing unconditionally
                await this._container.PatchItemAsync<CounterEntity>(key, new PartitionKey(key),
                    new PatchOperation[]
                    {
                        PatchOperation.Increment($"/count", -cost),
                    }
                );
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // Doing nothing
            }
        }

        internal class CounterEntity
        {
            public string id { get; set; } = default!;

            public long count { get; set; }

            public DateTimeOffset expiresAt { get; set; }

            public long nonce { get; set; }
        }

        private readonly Container _container;

        private async Task<long> CreateCounter(string key, long cost, DateTimeOffset ttl, long nonce)
        {
            var response = await this._container.CreateItemAsync(new CounterEntity
            {
                id = key,
                count = cost,
                expiresAt = ttl,
                nonce = nonce
            });

            return response.Resource.count;
        }

        private async Task<long> IncrementCounter(string key, long cost, DateTimeOffset ttl, long maxCounterValueToSetTtl, DateTimeOffset now, long nonce)
        {
            // Incrementing, if not expired
            var response = await this._container.PatchItemAsync<CounterEntity>(key, new PartitionKey(key),
                new PatchOperation[]
                {
                    PatchOperation.Increment($"/count", cost),
                },
                new PatchItemRequestOptions
                {
                    // Also checking nonce value, to prevent from colliding with Create operation
                    FilterPredicate = $"from c where c.expiresAt >= '{now.ToString("O")}' and c.count > 0 and c.nonce <> {nonce}"
                }
            );

            // Updating TTL, if needed
            var counter = response.Resource;
            if (counter.count <= maxCounterValueToSetTtl)
            {
                await this._container.PatchItemAsync<CounterEntity>(key, new PartitionKey(key),
                    new PatchOperation[]
                    {
                        PatchOperation.Set($"/expiresAt", ttl),
                    }
                );
            }

            return counter.count;
        }

        private async Task<long> ResetCounter(string key, long cost, DateTimeOffset ttl, DateTimeOffset now, long nonce)
        {
            // Resetting, if expired
            var response = await this._container.PatchItemAsync<CounterEntity>(key, new PartitionKey(key),
                new PatchOperation[]
                {
                    PatchOperation.Set($"/count", cost),
                    PatchOperation.Set($"/expiresAt", ttl),
                },
                new PatchItemRequestOptions
                {
                    // Also checking nonce value, to prevent from colliding with Create operation
                    FilterPredicate = $"from c where (c.expiresAt < '{now.ToString("O")}' or c.count < 1) and c.nonce <> {nonce}"
                }
            );

            var counter = response.Resource;
            return counter.count;
        }
    }
}