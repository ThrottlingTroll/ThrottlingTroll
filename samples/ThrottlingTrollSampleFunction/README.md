# ThrottlingTrollSampleFunction

This [Azure Functions (.NET Isolated)](https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-dotnet-isolated-overview) project demonstrates all the features of [ThrottlingTroll.AzureFunctions](https://www.nuget.org/packages/ThrottlingTroll.AzureFunctions).

## How to run locally

As a prerequisite, you will need [Azure Functions Core Tools globally installed](https://learn.microsoft.com/en-us/azure/azure-functions/functions-run-local#install-the-azure-functions-core-tools).

If you run this code on a GitHub Codespaces instance, then everything (including Redis server) should be pre-installed and ready for you.

1. (Optional, if you want to use **RedisCounterStore**) Add `RedisConnectionString` setting to [local.settings.json](https://github.com/scale-tone/ThrottlingTroll/blob/main/samples/ThrottlingTrollSampleFunction/local.settings.json) file. For a local Redis server that connection string usually looks like `localhost:6379`. 

2. Open your terminal in `samples/ThrottlingTrollSampleFunction` folder and type the following:
```
func start
```
