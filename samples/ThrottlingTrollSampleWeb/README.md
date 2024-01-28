# ThrottlingTrollSampleWeb

This ASP.NET project demonstrates all the features of [ThrottlingTroll](https://www.nuget.org/packages/ThrottlingTroll).

## How to run locally

As a prerequisite, you will need minimum [.NET 6.0](https://dotnet.microsoft.com/en-us/download/dotnet/6.0) installed.

If you run this code on a GitHub Codespaces instance, then everything (including Redis server) should be pre-installed and ready for you.

1. (Optional, if you want to use **RedisCounterStore**) Add `RedisConnectionString` setting to [appsettings.json](https://github.com/ThrottlingTroll/ThrottlingTroll/blob/main/samples/ThrottlingTrollSampleWeb/appsettings.json) file. For a local Redis server that connection string usually looks like `localhost:6379`. 

2. Open your terminal in `samples/ThrottlingTrollSampleWeb` folder and type the following:
```
dotnet run
```
