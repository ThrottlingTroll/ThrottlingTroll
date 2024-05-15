# ThrottlingTroll.AzureFunctionsAspNet

Rate limiting/throttling middleware for [Azure Functions with ASP.NET Core Integration](https://learn.microsoft.com/en-us/azure/azure-functions/dotnet-isolated-process-guide#aspnet-core-integration).

Install from NuGet:
```
dotnet add package ThrottlingTroll.AzureFunctionsAspNet
```

## How to use

Make sure you call one or another form of `.UseThrottlingTroll()` method at startup:
```
var builder = new HostBuilder();

// .....

builder.ConfigureFunctionsWebApplication((builderContext, workerAppBuilder) => {

    workerAppBuilder.UseThrottlingTroll();

    // .....
});
```

[All different ways of configuring, all other features and more code snippets are documented in our Wiki](https://github.com/ThrottlingTroll/ThrottlingTroll/wiki).


## Samples

[Here is a sample project, that demonstrates all the above concepts](https://github.com/ThrottlingTroll/ThrottlingTroll/tree/main/samples/ThrottlingTrollSampleAspNetFunction).
