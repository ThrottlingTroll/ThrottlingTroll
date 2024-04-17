# ThrottlingTroll.CounterStores.Redis

[ICounterStore](https://github.com/ThrottlingTroll/ThrottlingTroll/blob/main/ThrottlingTroll.Core/CounterStores/ICounterStore.cs) 
implementation that uses [Redis](https://redis.io).

Fast, consistent and reliable.

[<img alt="Nuget" src="https://img.shields.io/nuget/dt/ThrottlingTroll.CounterStores.Redis?label=ThrottlingTroll.CounterStores.Redis%20downloads">](https://www.nuget.org/packages/ThrottlingTroll.CounterStores.Redis)


## How to use

1. Install package from NuGet:
    ```
    dotnet add package ThrottlingTroll.CounterStores.Redis
    ```

2. EITHER put **RedisCounterStore** instance into your DI container:

    ```
    builder.Services.AddSingleton<ICounterStore>(
        new RedisCounterStore(ConnectionMultiplexer.Connect("my-redis-connection-string"))
    );
    ```
     OR provide an instance of it via **.UseThrottlingTroll()** method:

    ```
    app.UseThrottlingTroll(options =>
    {
        options.CounterStore = new RedisCounterStore(ConnectionMultiplexer.Connect("my-redis-connection-string"));
    });
    ```
