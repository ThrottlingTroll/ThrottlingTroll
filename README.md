# ThrottlingTroll

Rate limiting/throttling middleware for ASP.NET Core and Azure Functions.

[![.NET](https://github.com/ThrottlingTroll/ThrottlingTroll/actions/workflows/dotnet.yml/badge.svg)](https://github.com/ThrottlingTroll/ThrottlingTroll/actions/workflows/dotnet.yml)
[<img alt="Nuget" src="https://img.shields.io/nuget/v/ThrottlingTroll?label=current%20version">](https://www.nuget.org/packages/ThrottlingTroll)

[<img alt="Nuget" src="https://img.shields.io/nuget/dt/ThrottlingTroll?label=ThrottlingTroll%20downloads">](https://www.nuget.org/packages/ThrottlingTroll)
[<img alt="Nuget" src="https://img.shields.io/nuget/dt/ThrottlingTroll.AzureFunctions?label=ThrottlingTroll.AzureFunctions%20downloads">](https://www.nuget.org/packages/ThrottlingTroll.AzureFunctions)
[<img alt="Nuget" src="https://img.shields.io/nuget/dt/ThrottlingTroll.AzureFunctionsAspNet?label=ThrottlingTroll.AzureFunctionsAspNet%20downloads">](https://www.nuget.org/packages/ThrottlingTroll.AzureFunctionsAspNet)
 

Install from Nuget:

| ASP.NET Core                              | Azure Functions                                          | Azure Functions with ASP.NET Core Integration                 |
| -                                         | -                                                        | -                                                             |
| ```dotnet add package ThrottlingTroll```  | ```dotnet add package ThrottlingTroll.AzureFunctions```  |```dotnet add package ThrottlingTroll.AzureFunctionsAspNet```  |

## Features

* **Supports [ASP.NET Core](https://github.com/ThrottlingTroll/ThrottlingTroll/tree/main/ThrottlingTroll.AspNet#throttlingtroll), [Azure Functions (.NET Isolated)](https://github.com/ThrottlingTroll/ThrottlingTroll/tree/main/ThrottlingTroll.AzureFunctions#throttlingtrollazurefunctions) and [Azure Functions with ASP.NET Core Integration](https://github.com/ThrottlingTroll/ThrottlingTroll/tree/main/ThrottlingTroll.AzureFunctionsAspNet#throttlingtrollazurefunctionsaspnet)**. 
* [**Ingress throttling**](https://github.com/ThrottlingTroll/ThrottlingTroll/wiki#ingress-how-to-initialize-and-configure), aka let your service automatically respond with `429 TooManyRequests` to some obtrusive clients. 

   ```mermaid
      sequenceDiagram
          Client->>+YourService: #127760;HTTP
          alt limit exceeded?
              YourService-->>Client:❌ 429 TooManyRequests
          else
              YourService-->>-Client:✅ 200 OK
          end
   ```
   Implemented as an [ASP.NET Core Middleware](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware) (for ASP.NET Core) and as an [Azure Functions Middleware](https://learn.microsoft.com/en-us/azure/azure-functions/dotnet-isolated-process-guide#middleware) (for Azure Functions). 
     
* [**Egress throttling**](https://github.com/ThrottlingTroll/ThrottlingTroll/wiki#egress-how-to-initialize-and-configure), aka limit the number of calls your code is making against some external endpoint. 

   ```mermaid
      sequenceDiagram
          YourService->>+HttpClient: SendAsync()
          alt limit exceeded?
              HttpClient-->>YourService:❌ 429 TooManyRequests
          else
              HttpClient->>+TheirService: #127760;HTTP
              TheirService-->>-HttpClient:✅ 200 OK
              HttpClient-->>-YourService:✅ 200 OK
          end
   ```
   Implemented as an [HttpClient DelegatingHandler](https://learn.microsoft.com/en-us/aspnet/web-api/overview/advanced/httpclient-message-handlers#custom-message-handlers), which produces `429 TooManyRequests` response (without making the actual call) when a limit is exceeded.
   
* [**Propagating `429 TooManyRequests` from egress to ingress**](https://github.com/ThrottlingTroll/ThrottlingTroll/wiki/240.-%5BEgress%5D-How-to-use-egress%E2%80%90to%E2%80%90ingress-propagation), aka when your service internally makes an HTTP request which results in `429 TooManyRequests`, your service can automatically respond with same `429 TooManyRequests` to its calling client.

   ```mermaid
      sequenceDiagram
          Client->>+YourService: #127760;HTTP
          YourService->>+TheirService: #127760;HTTP
          TheirService-->>-YourService:❌ 429 TooManyRequests
          YourService-->>-Client:❌ 429 TooManyRequests
   ```

* **Custom response fabrics**. [For ingress it gives full control on what to return when a request is being throttled, and also allows to implement delayed responses (instead of just returning `429 TooManyRequests`)](https://github.com/ThrottlingTroll/ThrottlingTroll/wiki/140.-%5BIngress%5D-Delays,-spin%E2%80%90waits-and-custom-responses): 

   ```mermaid
      sequenceDiagram
          Client->>+YourService: #127760;HTTP
          alt limit exceeded?
              YourService-->>YourService: await Task.Delay(RetryAfter)
              YourService-->>Client:✅ 200 OK
          else
              YourService-->>-Client:✅ 200 OK
          end
   ```

   [For egress it also allows ThrottlingTroll to do automatic retries for you](https://github.com/ThrottlingTroll/ThrottlingTroll/wiki/250.-%5BEgress%5D-Delays,-spin%E2%80%90waits-and-custom-responses):

   ```mermaid
      sequenceDiagram
          YourService->>+HttpClient: SendAsync()

          loop while 429 TooManyRequests
              HttpClient->>+TheirService: #127760;HTTP
              TheirService-->>-HttpClient:❌ 429 TooManyRequests
              HttpClient-->>HttpClient: await Task.Delay(RetryAfter)
          end

          HttpClient->>+TheirService: #127760;HTTP
          TheirService-->>-HttpClient:✅ 200 OK
          HttpClient-->>-YourService:✅ 200 OK
   ```

* [**Storing rate counters in a distributed cache**](https://github.com/ThrottlingTroll/ThrottlingTroll/wiki/510.-Counter-Stores), making your rate limiting policy consistent across all your computing instances. Supported distributed counter stores are:
  * [ThrottlingTroll.CounterStores.Redis](https://github.com/ThrottlingTroll/ThrottlingTroll/wiki/510.-Counter-Stores#-throttlingtrollcounterstoresredis)
  * [ThrottlingTroll.CounterStores.AzureTable](https://github.com/ThrottlingTroll/ThrottlingTroll/wiki/510.-Counter-Stores#-throttlingtrollcounterstoresazuretable)
  * [ThrottlingTroll.CounterStores.DistributedCache](https://github.com/ThrottlingTroll/ThrottlingTroll/wiki/510.-Counter-Stores#-throttlingtrollcounterstoresdistributedcache)

  And [you can implement your own](https://github.com/ThrottlingTroll/ThrottlingTroll/blob/main/ThrottlingTroll.Core/CounterStores/ICounterStore.cs).

* [**Four ways of configuring**](https://github.com/ThrottlingTroll/ThrottlingTroll/wiki#ingress-how-to-initialize-and-configure):
  * [Declaratively, aka using ThrottlingTrollAttribute](https://github.com/ThrottlingTroll/ThrottlingTroll/wiki/105.-%5BIngress%5D-How-to-configure-declaratively-(with-attributes)). Aims for best readability.
  * [Statically, aka via `appsettings.json/host.json`](https://github.com/ThrottlingTroll/ThrottlingTroll/wiki/110.-%5BIngress%5D-How-to-configure-statically-(via-config-files)). Simplest.
  * [Programmatically, at startup](https://github.com/ThrottlingTroll/ThrottlingTroll/wiki/120.-%5BIngress%5D-How-to-configure-programmatically). In case you want to parametrize something.
  * [Reactively](https://github.com/ThrottlingTroll/ThrottlingTroll/wiki/130.-%5BIngress%5D-How-to-configure-reactively). You provide a routine, that fetches limits from wherever, and an **IntervalToReloadConfigInSeconds** for that routine to be called periodically. Allows to reconfigure rules and limits on-the-fly, *without restarting your service*.
 
    And you can combine all four approaches in the same solution.

* [**IdentityIdExtractor**s](https://github.com/ThrottlingTroll/ThrottlingTroll/wiki/150.-%5BIngress%5D-Personalized-rate-limiting), that allow you to limit clients individually, based on their IP-addresses, api-keys, tokens, headers, query strings, claims etc. etc.

* [**CostExtractor**s](https://github.com/ThrottlingTroll/ThrottlingTroll/wiki/150.-%5BIngress%5D-Personalized-rate-limiting#using-costextractors), that you can use to assign custom *costs* to different requests. Default cost is **1**, but if some of your requests are heavier than the others, you can assign higher costs to them.
  Another typical usecase for this would be to arrange different *pricing tiers* for your service: you set the rate limit to something high - and then "charge" clients differently, based on their pricing tier.


## Supported rate limiting algorithms

* [**FixedWindow**](https://github.com/ThrottlingTroll/ThrottlingTroll/wiki/410.-Rate-Limiting-Algorithms#-fixedwindow). No more than **PermitLimit** requests are allowed in **IntervalInSeconds**. Here is an illustration for the case of no more than 2 requests per each 8 seconds:
    
     <img src="https://github.com/ThrottlingTroll/ThrottlingTroll/assets/5447190/ffb0bdc8-736b-4c6f-9eb4-db54ce72e034" height="300px"/>

  The typical drawback of FixedWindow algorithm is that you'd get request rate *bursts* at the end of each window. So specifically to cope that we have

* [**SlidingWindow**](https://github.com/ThrottlingTroll/ThrottlingTroll/wiki/410.-Rate-Limiting-Algorithms#-slidingwindow). No more than **PermitLimit** requests are allowed in **IntervalInSeconds**, but that interval is split into **NumOfBuckets**. The main benefit of this algorithm over **FixedWindow** is that if a client constantly exceedes **PermitLimit**, it will never get any valid response and will always get `429 TooManyRequests`. Here is an illustration for the case of no more than 2 requests per each 8 seconds with 2 buckets:
    
     <img src="https://github.com/ThrottlingTroll/ThrottlingTroll/assets/5447190/e18abb9c-d1dd-4b64-a007-220605ed03e9" height="300px"/>  

  In other words, with SlidingWindow your service gets a *smoother request rate*.
          
* [**Semaphore**](https://github.com/ThrottlingTroll/ThrottlingTroll/wiki/410.-Rate-Limiting-Algorithms#-semaphore) aka Concurrency Limiter. No more than **PermitLimit** requests are allowed to be executed **concurrently**. Here is an illustration for the case of no more than 3 concurrent requests:

     <img src="https://github.com/ThrottlingTroll/ThrottlingTroll/assets/5447190/0beeac73-5d35-482a-a790-a3fe9ea6e38b" height="300px"/>  
   
     If you set Semaphore's **PermitLimit** to  **1** and use  **RedisCounterStore**, then ThrottlingTroll will act as a distributed lock. If you add an **IdentityIdExtractor** (identifying requests by e.g. a query string parameter), then it will turn into *named* distributed locks. 


## [Documentation](https://github.com/ThrottlingTroll/ThrottlingTroll/wiki)

[You can find it in our Wiki](https://github.com/ThrottlingTroll/ThrottlingTroll/wiki).

Most concepts and features are the same for all supported platforms. Things that are specific to each platform are highlighted in the relevant READMEs:

| ASP.NET Core                              | Azure Functions                                          | Azure Functions with ASP.NET Core Integration |
| -                                         | -                                                        | -                                             |
| [How to use with ASP.NET Core](https://github.com/ThrottlingTroll/ThrottlingTroll/tree/main/ThrottlingTroll.AspNet#how-to-configure) | [How to use with Azure Functions](https://github.com/ThrottlingTroll/ThrottlingTroll/tree/main/ThrottlingTroll.AzureFunctions#how-to-configure) | [How to use with Azure Functions ASP.NET Core Integration](https://github.com/ThrottlingTroll/ThrottlingTroll/tree/main/ThrottlingTroll.AzureFunctionsAspNet#how-to-configure) |


## Samples

Full minimalistic sample using ASP.NET Core Minimal API:

```
using ThrottlingTroll;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "Hello ThrottlingTroll!");

// Limiting to 1 request per 2 seconds
app.UseThrottlingTroll(options =>
{
    options.Config = new ThrottlingTrollConfig 
    {
        Rules =
        [
            new ThrottlingTrollRule
            {
                LimitMethod = new FixedWindowRateLimitMethod
                {
                    PermitLimit = 1,
                    IntervalInSeconds = 2
                }
            }
        ]
    };
});

app.Run();
```

Sample projects that demonstrate all the above concepts:

| ASP.NET Core | Azure Functions |
| -            | -               |
| [ThrottlingTrollSampleWeb](https://github.com/ThrottlingTroll/ThrottlingTroll/tree/main/samples/ThrottlingTrollSampleWeb) | [ThrottlingTrollSampleFunction](https://github.com/ThrottlingTroll/ThrottlingTroll/tree/main/samples/ThrottlingTrollSampleFunction)  |
|                                                                                                                      | [ThrottlingTrollSampleAspNetFunction](https://github.com/ThrottlingTroll/ThrottlingTroll/tree/main/samples/ThrottlingTrollSampleAspNetFunction)  
|                                                                                                                      | [ThrottlingTrollSampleDotNet6InProcDurableFunction](https://github.com/ThrottlingTroll/ThrottlingTroll/tree/main/samples/ThrottlingTrollSampleDotNet6InProcDurableFunction)  


## Contributing

Is very much welcomed.
