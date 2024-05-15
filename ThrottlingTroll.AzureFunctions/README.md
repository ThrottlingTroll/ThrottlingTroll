# ThrottlingTroll.AzureFunctions

Rate limiting/throttling middleware for Azure Functions (.NET Isolated).

Install from NuGet:
```
dotnet add package ThrottlingTroll.AzureFunctions
```

IMPORTANT: if in your project you are using [ASP.NET Core Integration](https://learn.microsoft.com/en-us/azure/azure-functions/dotnet-isolated-process-guide#aspnet-core-integration), then you need to install and use [ThrottlingTroll.AzureFunctionsAspNet](https://github.com/ThrottlingTroll/ThrottlingTroll/tree/main/ThrottlingTroll.AzureFunctionsAspNet#throttlingtrollazurefunctionsaspnet) package instead.

## How to use

Make sure you call one or another form of `.UseThrottlingTroll()` method at startup:
```
var builder = new HostBuilder();

// .....

builder.ConfigureFunctionsWorkerDefaults((hostBuilderContext, workerAppBuilder) => {

    workerAppBuilder.UseThrottlingTroll();

    // .....
});
```

[All different ways of configuring, all other features and more code snippets are documented in our Wiki](https://github.com/ThrottlingTroll/ThrottlingTroll/wiki).


## Samples

[Here is a sample project, that demonstrates how to use ThrottlingTroll in an Azure Functions (.NET Isolated) project](https://github.com/ThrottlingTroll/ThrottlingTroll/tree/main/samples/ThrottlingTrollSampleFunction).
