using Azure;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ThrottlingTroll.CounterStores.AzureTable
{
    /// <summary>
    /// Implements Store for rate limit counters using Azure Tables / Cosmos DB for Table
    /// </summary>
    public class CosmosDbCounterStore : ICounterStore
    {
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
            try
            {
                var response = await this._container.PatchItemAsync<CounterEntity>(key, new PartitionKey(key), new PatchOperation[]
                {
                    PatchOperation.Increment($"/count", cost)
                });


            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                var createResponse = await this._container.CreateItemAsync(new CounterEntity
                {
                    id = key,
                    count = cost,
                    expiresAt = ttl
                });

                return 0;
            }

            return 0;
        }

        /// <inheritdoc />
        public async Task DecrementAsync(string key, long cost, IHttpRequestProxy request)
        {
        }

        internal class CounterEntity
        {
            public string id { get; set; } = default!;

            public long count { get; set; }

            public DateTimeOffset expiresAt { get; set; }
        }

        private readonly Container _container;
    }
}