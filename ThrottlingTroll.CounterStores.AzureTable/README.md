# ThrottlingTroll.CounterStores.AzureTable

[ICounterStore](https://github.com/scale-tone/ThrottlingTroll/blob/main/ThrottlingTroll.Core/CounterStores/ICounterStore.cs) 
implementation that uses Azure Tables (or Cosmos DB with Table API).

Easiest to configure (only takes a Storage connection string), but not recommended for production workloads, 
because contention around the table might get very high (remember that Azure Storage Tables only support 
optimistic concurrency and no atomic operations).

## How to use

1. Install package from NuGet:
    ```
    dotnet add package ThrottlingTroll.CounterStores.AzureTable
    ```

2. EITHER put **AzureTableCounterStore** instance into your DI container:

    ```
    builder.Services.AddSingleton<ICounterStore>(
        new AzureTableCounterStore("my-azure-storage-connection-string", "ThrottlingTrollCountersTable")
    );
    ```
     OR provide an instance of it via **.UseThrottlingTroll()** method:

    ```
    app.UseThrottlingTroll(options => {
        options.CounterStore = new AzureTableCounterStore("my-azure-storage-connection-string", "ThrottlingTrollCountersTable");
    });
    ```
    
The second parameter - table name - can be anything you like. That table will be auto-created, if not exists yet.

IMPORTANT: if you use Cosmos DB, then its default consistency level should be set to either `STRONG` or `BOUNDED STALENESS`.
