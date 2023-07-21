# ThrottlingTroll.CounterStores.AzureTable

[ICounterStore](https://github.com/scale-tone/ThrottlingTroll/blob/main/ThrottlingTroll.Core/CounterStores/ICounterStore.cs) 
implementation that uses Azure Tables (or Cosmos DB with Table API).

Easiest to configure (only takes a Storage connection string), but not recommended for production workloads, 
because contention around the table might get very high (remember that Azure Storage Tables only support 
optimistic concurrency and no atomic operations).
