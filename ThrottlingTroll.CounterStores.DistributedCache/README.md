# ThrottlingTroll.CounterStores.DistributedCache

[ICounterStore](https://github.com/ThrottlingTroll/ThrottlingTroll/blob/main/ThrottlingTroll.Core/CounterStores/ICounterStore.cs) 
implementation that uses [ASP.NET Core's IDistributedCache](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-7.0#idistributedcache-interface).

Fast, but not entirely consistent (because IDistributedCache does not provide atomic operations).
In other words, your computing instances might go slightly on their own. 
For true consistency consider using [ThrottlingTroll.CounterStores.Redis](https://github.com/ThrottlingTroll/ThrottlingTroll/tree/main/ThrottlingTroll.CounterStores.Redis) instead.

## How to use

1. Install package from NuGet:
    ```
    dotnet add package ThrottlingTroll.CounterStores.DistributedCache
    ```
    
2. Configure an **IDistributedCache** option of your choice [as described here](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed#establish-distributed-caching-services).

2. EITHER put **DistributedCacheCounterStore** instance into your DI container:

    ```
    builder.Services.AddSingleton<ICounterStore>(provider =>
        new DistributedCacheCounterStore(provider.GetRequiredService<IDistributedCache>())
    );
    ```
     OR provide an instance of it via **.UseThrottlingTroll()** method:

    ```
    app.UseThrottlingTroll(options => {
        options.CounterStore = new DistributedCacheCounterStore(app.Services.GetRequiredService<IDistributedCache>());
    });
    ```
