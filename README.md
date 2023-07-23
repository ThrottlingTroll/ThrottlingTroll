# ThrottlingTroll

Rate limiting/throttling middleware for ASP.NET and Azure Functions.

[![.NET](https://github.com/scale-tone/ThrottlingTroll/actions/workflows/dotnet.yml/badge.svg)](https://github.com/scale-tone/ThrottlingTroll/actions/workflows/dotnet.yml)
[<img alt="Nuget" src="https://img.shields.io/nuget/v/ThrottlingTroll?label=current%20version">](https://www.nuget.org/packages/ThrottlingTroll)
[<img alt="Nuget" src="https://img.shields.io/nuget/dt/ThrottlingTroll?label=ThrottlingTroll%20downloads">](https://www.nuget.org/packages/ThrottlingTroll)
[<img alt="Nuget" src="https://img.shields.io/nuget/dt/ThrottlingTroll.AzureFunctions?label=ThrottlingTroll.AzureFunctions%20downloads">](https://www.nuget.org/packages/ThrottlingTroll.AzureFunctions)
 

Install from Nuget:

| ASP.NET                                   | Asure Functions                                          |
| -                                         | -                                                        |
| ```dotnet add package ThrottlingTroll```  | ```dotnet add package ThrottlingTroll.AzureFunctions```  |

## Features

* **Both [ASP.NET](https://github.com/scale-tone/ThrottlingTroll/tree/main/ThrottlingTroll.AspNet#throttlingtroll) and [Azure Functions (.NET Isolated)](https://github.com/scale-tone/ThrottlingTroll/tree/main/ThrottlingTroll.AzureFunctions#throttlingtrollazurefunctions) are supported**. 
* **Ingress throttling**, aka let your service automatically respond with `429 TooManyRequests` to some obtrusive clients. 

   ```mermaid
      sequenceDiagram
          Client->>+YourService: #127760;HTTP
          alt limit exceeded?
              YourService-->>Client:❌ 429 TooManyRequests
          else
              YourService-->>-Client:✅ 200 OK
          end
   ```
   Implemented as an [ASP.NET Core Middleware](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware) (for ASP.NET) and as an [Azure Functions Middleware](https://learn.microsoft.com/en-us/azure/azure-functions/dotnet-isolated-process-guide#middleware) (for Azure Functions). 
     
* **Egress throttling**, aka limit the number of calls your code is making against some external endpoint. 

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
   
* **Propagating `429 TooManyRequests` from egress to ingress**, aka when your service internally makes an HTTP request which results in `429 TooManyRequests`, your service can automatically respond with same `429 TooManyRequests` to its calling client.

   ```mermaid
      sequenceDiagram
          Client->>+YourService: #127760;HTTP
          YourService->>+TheirService: #127760;HTTP
          TheirService-->>-YourService:❌ 429 TooManyRequests
          YourService-->>-Client:❌ 429 TooManyRequests
   ```

* **Custom response fabrics**. For ingress it gives full control on what to return when a request is being throttled, and also allows to implement delayed responses (instead of just returning `429 TooManyRequests`): 

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

   For egress it also allows ThrottlingTroll to do automatic retries for you:

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

* **Storing rate counters in a distributed cache**, making your throttling policy consistent across all your computing instances. Supported distributed counter stores are:
  * [ThrottlingTroll.CounterStores.Redis](https://github.com/scale-tone/ThrottlingTroll/tree/main/ThrottlingTroll.CounterStores.Redis)
  * [ThrottlingTroll.CounterStores.AzureTable](https://github.com/scale-tone/ThrottlingTroll/tree/main/ThrottlingTroll.CounterStores.AzureTable)
  * [ThrottlingTroll.CounterStores.DistributedCache](https://github.com/scale-tone/ThrottlingTroll/tree/main/ThrottlingTroll.CounterStores.DistributedCache)

* **Dynamically configuring rate limits**, so that those limits can be adjusted on-the-go, without restarting the service.

* **IdentityIdExtractor**s, that allow you to limit clients individually, based on their IP-addresses, api-keys, tokens, headers, query strings, claims etc. etc.

## Supported rate limiting algorithms

* **FixedWindow**. No more than **PermitLimit** requests are allowed in **IntervalInSeconds**. Here is an illustration for the case of no more than 2 requests per each 8 seconds:
    
     <img src="https://github.com/scale-tone/ThrottlingTroll/assets/5447190/ffb0bdc8-736b-4c6f-9eb4-db54ce72e034" height="300px"/>

* **SlidingWindow**. No more than **PermitLimit** requests are allowed in **IntervalInSeconds**, but that interval is split into **NumOfBuckets**. The main benefit of this algorithm over **FixedWindow** is that if a client constantly exceedes **PermitLimit**, it will never get any valid response and will always get `429 TooManyRequests`. Here is an illustration for the case of no more than 2 requests per each 8 seconds with 2 buckets:
    
     <img src="https://github.com/scale-tone/ThrottlingTroll/assets/5447190/e18abb9c-d1dd-4b64-a007-220605ed03e9" height="300px"/>  
          
* **Semaphore** aka Concurrency Limiter. No more than **PermitLimit** requests are allowed to be executed **concurrently**. Here is an illustration for the case of no more than 3 concurrent requests:

     <img src="https://github.com/scale-tone/ThrottlingTroll/assets/5447190/0beeac73-5d35-482a-a790-a3fe9ea6e38b" height="300px"/>  
   
     If you set Semaphore's **PermitLimit** to  **1** and use  **RedisCounterStore**, then ThrottlingTroll will act as a distributed lock. If you add an **IdentityIdExtractor** (identifying requests by e.g. a query string parameter), then it will turn into *named* distributed locks. 



## How to configure and use

Configuration and usage with ASP.NET and Azure Functions is very similar yet slightly different:

| ASP.NET                                   | Asure Functions                                          |
| -                                         | -                                                        |
| [How to use with ASP.NET](https://github.com/scale-tone/ThrottlingTroll/tree/main/ThrottlingTroll.AspNet#how-to-configure) | [How to use with Azure Functions](https://github.com/scale-tone/ThrottlingTroll/tree/main/ThrottlingTroll.AzureFunctions#how-to-configure) |


## Samples

Sample projects that demonstrate all the above concepts:

| ASP.NET | Asure Functions |
| -       | -               |
| [ThrottlingTrollSampleWeb](https://github.com/scale-tone/ThrottlingTroll/tree/main/samples/ThrottlingTrollSampleWeb) | [ThrottlingTrollSampleFunction](https://github.com/scale-tone/ThrottlingTroll/tree/main/samples/ThrottlingTrollSampleFunction)  |


## Contributing

Is very much welcomed.
