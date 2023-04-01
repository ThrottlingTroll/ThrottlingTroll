# ThrottlingTroll

Rate limiting/throttling middleware for ASP.NET and Azure Functions.

[<img alt="Nuget" src="https://img.shields.io/nuget/v/ThrottlingTroll?label=current%20version">](https://www.nuget.org/packages/ThrottlingTroll) [![.NET](https://github.com/scale-tone/ThrottlingTroll/actions/workflows/dotnet.yml/badge.svg)](https://github.com/scale-tone/ThrottlingTroll/actions/workflows/dotnet.yml)

Install from Nuget:

| ASP.NET                                   | Asure Functions                                          |
| -                                         | -                                                        |
| ```dotnet add package ThrottlingTroll```  | ```dotnet add package ThrottlingTroll.AzureFunctions```  |

## Features

* **Both ASP.NET and Azure Functions (.NET Isolated) are supported**. 
* **Ingress throttling**, aka let your service automatically respond with `429 TooManyRequests` to some obtrusive clients. Implemented as an [ASP.NET Core Middleware](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware). 
* **Egress throttling**, aka limit the number of calls your code is making against some external endpoint. Implemented as an [HttpClient DelegatingHandler](https://learn.microsoft.com/en-us/aspnet/web-api/overview/advanced/httpclient-message-handlers#custom-message-handlers), which produces `429 TooManyRequests` response (without making the actual call) when a limit is exceeded.
* **Storing rate counters in a distributed cache**, making your throttling policy consistent across all your computing instances. Both [Microsoft.Extensions.Caching.Distributed.IDistributedCache](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-7.0#idistributedcache-interface) and [StackExchange.Redis](https://stackexchange.github.io/StackExchange.Redis/Basics.html) are supported. 
* **Propagating `429 TooManyRequests` from egress to ingress**, aka when your service internally makes an HTTP request which results in `429 TooManyRequests`, your service can automatically respond with same `429 TooManyRequests` to its calling client.
* **Dynamically configuring rate limits**, so that those limits can be adjusted on-the-go, without restarting the service.
* **Custom response fabrics**. For ingress it gives full control on what to return when a request is being throttled, and also allows to implement delayed responses (instead of just returning `429 TooManyRequests`). For egress it also allows ThrottlingTroll to do automatic retries for you.

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
