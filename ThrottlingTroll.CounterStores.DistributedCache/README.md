# ThrottlingTroll.CounterStores.DistributedCache

[ICounterStore](https://github.com/scale-tone/ThrottlingTroll/blob/main/ThrottlingTroll.Core/CounterStores/ICounterStore.cs) 
implementation that uses [ASP.NET Core's IDistributedCache](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-7.0#idistributedcache-interface).

Fast, but not entirely consistent (because IDistributedCache does not provide atomic operations).
In other words, your computing instances might go slightly on their own. 
For true consistency consider using [ThrottlingTroll.CounterStores.Redis](https://github.com/scale-tone/ThrottlingTroll/tree/main/ThrottlingTroll.CounterStores.Redis) instead.
