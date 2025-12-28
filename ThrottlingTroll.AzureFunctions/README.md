# ThrottlingTroll.AzureFunctions

Rate limiting/throttling middleware for Azure Functions. Can be used both in InProc and Isolated projects, both with "classic" HTTP-triggered Functions and with [ASP.Net Core Integration](https://learn.microsoft.com/en-us/azure/azure-functions/dotnet-isolated-process-guide#aspnet-core-integration).

Install from NuGet:
```
dotnet add package ThrottlingTroll.AzureFunctions
```

## How to use (.NET Isolated)

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


## How to use (.NET InProc)

1. Use `.AddThrottlingTroll()` method to add ThrottlingTroll to your DI container at startup:
```
[assembly: WebJobsStartup(typeof(ThrottlingTrollSampleInProcFunction.Startup.Startup))]
namespace ThrottlingTrollSampleInProcFunction.Startup
{
    public class Startup : IWebJobsStartup
    {
        public void Configure(IWebJobsBuilder builder)
        {
            builder.Services.AddThrottlingTroll(options =>
            {
                // In InProc Functions config sections loaded from host.json have the "AzureFunctionsJobHost:" prefix in their names
                options.ConfigSectionName = "AzureFunctionsJobHost:ThrottlingTrollIngress";

                // .....
            });
        }
    }
}
```

2. Add [InProc-specific implementation of IHttpRequestProxy](https://github.com/ThrottlingTroll/ThrottlingTroll-AzureFunctions-Samples/blob/main/ThrottlingTrollSampleInProcFunction/InProcHttpRequestProxy.cs) to your project.

3. Wrap your Functions with `.WithThrottlingTroll()` method, like this:
```
public class MyFunctions
{
    private readonly IThrottlingTroll _thtr;

    public Functions(IThrottlingTroll thtr)
    {
        this._thtr = thtr;
    }

    [FunctionName("MyFunc")]
    public Task<IActionResult> MyFunc([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
        => this._thtr.WithThrottlingTroll(new InProcHttpRequestProxy(req),
            async ctx =>
            {
                // Your code goes here ...

                return (IActionResult)new OkObjectResult("OK");
            },
            async ctx =>
            {
                return (IActionResult)new StatusCodeResult((int)HttpStatusCode.TooManyRequests);
            });
}
``` 

## Samples

[Sample Azure Functions projects (both InProc and Isolated) are located in this separate repo](https://github.com/ThrottlingTroll/ThrottlingTroll-AzureFunctions-Samples).
