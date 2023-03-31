# ThrottlingTroll

Yet another take on rate limiting/throttling in ASP.NET.

[<img alt="Nuget" src="https://img.shields.io/nuget/v/ThrottlingTroll?label=current%20version">](https://www.nuget.org/packages/ThrottlingTroll) [![.NET](https://github.com/scale-tone/ThrottlingTroll/actions/workflows/dotnet.yml/badge.svg)](https://github.com/scale-tone/ThrottlingTroll/actions/workflows/dotnet.yml)

Install it from NuGet:
```
dotnet add package ThrottlingTroll
```

## Features

* **Ingress throttling**, aka let your service automatically respond with `429 TooManyRequests` to some obtrusive clients. Implemented as an [ASP.NET Core Middleware](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware). 
* **Egress throttling**, aka limit the number of calls your code is making against some external endpoint. Implemented as an [HttpClient DelegatingHandler](https://learn.microsoft.com/en-us/aspnet/web-api/overview/advanced/httpclient-message-handlers#custom-message-handlers), which produces `429 TooManyRequests` response (without making the actual call) when a limit is exceeded.
* **Storing rate counters in a distributed cache**, making your throttling policy consistent across all your computing instances. Both [Microsoft.Extensions.Caching.Distributed.IDistributedCache](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-7.0#idistributedcache-interface) and [StackExchange.Redis](https://stackexchange.github.io/StackExchange.Redis/Basics.html) are supported. 
* **Propagating `429 TooManyRequests` from egress to ingress**, aka when your service internally makes an HTTP request which results in `429 TooManyRequests`, your service can automatically respond with same `429 TooManyRequests` to its calling client.
* **Dynamically configuring rate limits**, so that those limits can be adjusted on-the-go, without restarting the service.
* **Custom response fabrics**. For ingress it gives full control on what to return when a request is being throttled, and also allows to implement delayed responses (instead of just returning `429 TooManyRequests`). For egress it also allows ThrottlingTroll to do automatic retries for you.

## How to configure

Quick example of an ingress config setting:
```
  "ThrottlingTrollIngress": {
    "Rules": [
      {
        "UriPattern": "/api/values",
        "RateLimit": {
          "Algorithm": "FixedWindow",
          "PermitLimit": 5,
          "IntervalInSeconds": 10
        }
      }
    ]
  }
```

ThrottlingTroll's configuration (both for ingress and egress) is represented by [ThrottlingTrollConfig](https://github.com/scale-tone/ThrottlingTroll/blob/main/ThrottlingTroll/ThrottlingTrollConfig.cs) class.
It contains a list of rate limiting Rules and some other settings and it can be configured:

* Statically, via appsettings.json.
* Programmatically, at service startup.
* Dynamically, by providing a callback, which ThrottlingTroll will periodically call to get updated values.

See more examples for all of these options below.

### Configuring rules and limits

Each Rule defines a pattern that HTTP requests should match. A pattern can include the following properties (all are optional):
* **UriPattern** - a Regex pattern to match request URI against. Empty string or null means any URI. **Note that this value is treated as a Regex**, so symbols that have special meaning in Regex language must be escaped (e.g. to match a query string specify `\\?abc=123` instead of `?abc=123`).
* **Method** - request's HTTP method. E.g. `POST`. Empty string or null means any method.
* **HeaderName** - request's HTTP header to check. If specified, the rule will only apply to requests with this header set to **HeaderValue**.
* **HeaderValue** - value for HTTP header identified by **HeaderName**. The rule will only apply to requests with that header set to this value. If **HeaderName** is specified and **HeaderValue** is not - that matches requests with any value in that header.
* **IdentityId** - request's custom Identity ID. If specified, the rule will only apply to requests with this Identity ID. Along with **IdentityId** you will also need to provide a custom identity extraction routine via  **IdentityIdExtractor** setting.
* **IdentityIdExtractor** - a routine that takes a request object and should return the caller's identityId. Typically what serves as identityId is some api-key (taken from e.g. a header), or client's IP address, or the **oid** claim of a JWT token, or something similar. When **IdentityIdExtractor** is specified and **IdentityId** is not - different identityIds will automatically get their own rate counters, one counter per each unique identityId.

If any of the above properties is empty or not specified, this means matching **any** request.

Then each Rule must specify the rate limiting algorithm to be applied for matched requests and its parameters.

The following algorithms are currently supported:
* **FixedWindow**. No more than **PermitLimit** requests are allowed in **IntervalInSeconds**. Example:
```
  "ThrottlingTrollIngress": {
    "Rules": [
      {
        "RateLimit": {
          "Algorithm": "FixedWindow",
          "PermitLimit": 5,
          "IntervalInSeconds": 10
        }
      }
    ]
  }
```

* **SlidingWindow**. No more than **PermitLimit** requests are allowed in **IntervalInSeconds**, but that interval is split into **NumOfBuckets**. The main benefit of this algorithm over **FixedWindow** is that if a client constantly exceedes **PermitLimit**, it will never get any valid response and will always get `429 TooManyRequests`. Example:
```
  "ThrottlingTrollIngress": {
    "Rules": [
      {
        "RateLimit": {
          "Algorithm": "SlidingWindow",
          "PermitLimit": 5,
          "IntervalInSeconds": 15,
          "NumOfBuckets": 3
        }
      }
    ]
  }
```

### Configuring whitelist

Requests that should be whitelisted (exempt from the above Rules) can be specified via **WhiteList** property:
```
  "ThrottlingTrollIngress": {

    "WhiteList": [
      "/api/healthcheck",
      "api-key=my-unlimited-api-key"
    ]
  },
```

### Specifying UniqueName property when sharing a distributed cache instance

When using the same instance of a distributed cache for multiple different services you might also need to specify a value for **UniqueName** property: 
```
  "ThrottlingTrollIngress": {

    "UniqueName": "MyThrottledService123"
  }
```

That value will be used as a prefix for cache keys, so that those multiple services do not corrupt each other's rate counters.


## How to use for Ingress Throttling

### To configure via appsettings.json

1. Add the following `ThrottlingTrollIngress` section to your config file:
```
  "ThrottlingTrollIngress": {

    "Rules": [
        ... here go rate limiting rules and limits...
    ],

    "WhiteList": [
        ... here go whitelisted URIs...
    ]
  }

```

2. Add the following call to your startup code:
```
app.UseThrottlingTroll();
```

### To configure programmatically

Use the following configuration method at startup:
```
app.UseThrottlingTroll(options =>
{
    options.Config = new ThrottlingTrollConfig
    {
        Rules = new[]
        {
            new ThrottlingTrollRule
            {
                UriPattern = "/api/values",
                LimitMethod = new SlidingWindowRateLimitMethod
                {
                    PermitLimit = 5,
                    IntervalInSeconds = 10,
                    NumOfBuckets = 5
                }
            },

            // add more rules here...
        }
    };
});

```

### To configure dynamically

Specify a callback that loads rate limits from some shared persistent storage and a time interval to periodically call it:
```
app.UseThrottlingTroll(options =>
{
    options.GetConfigFunc = async () =>
    {
        var myThrottlingRules = await LoadThrottlingRulesFromDatabase();

        return new ThrottlingTrollConfig
        {
            Rules = myThrottlingRules
        };
    };

    options.IntervalToReloadConfigInSeconds = 10;
});
```

NOTE: if your callback throws an exception, ThrottlingTroll will get suspended (will not apply any rules) until the callback succeeds again.

### To limit clients based on their identityId

If you have a custom way to identify your clients and you want to limit their calls individually, specify a custom **IdentityIdExtractor** routine like this:
```
app.UseThrottlingTroll(options =>
{
    options.Config = new ThrottlingTrollConfig
    {
        Rules = new[]
        {
            new ThrottlingTrollRule
            {
                LimitMethod = new FixedWindowRateLimitMethod
                {
                    PermitLimit = 3,
                    IntervalInSeconds = 15
                },

                IdentityIdExtractor = (request) =>
                {
                    // Identifying clients e.g. by their api-key
                    return request.IncomingRequest.Query["api-key"];
                }
            }
        }                    
    };
});
```
Then ThrottlingTroll will count their requests on a per-identity basis.

### To customize responses with a custom response fabric

Provide a response fabric implementation via **ResponseFabric** option:
```
app.UseThrottlingTroll(options =>
{
    // Custom response fabric, returns 400 BadRequest + some custom content
    options.ResponseFabric = async (limitExceededResult, requestProxy, responseProxy, requestAborted) => 
    {
        responseProxy.IngressResponse.StatusCode = StatusCodes.Status400BadRequest;

        responseProxy.IngressResponse.Headers.Add(HeaderNames.RetryAfter, limitExceededResult.RetryAfterHeaderValue);

        await responseProxy.IngressResponse.WriteAsync("Too many requests. Try again later.");
    };
});
```

### To delay responses instead of returning errors

Provide a response fabric implementation with a delay in it. Also set **ShouldContinueWithIngressAsNormal** to **true** (this will make ThrottlingTroll do the normal request processing instead of shortcutting to a 429 status) :
```
app.UseThrottlingTroll(options =>
{
    // Custom response fabric, impedes the normal response for 3 seconds
    options.ResponseFabric = async (limitExceededResult, requestProxy, responseProxy, requestAborted) =>
    {
        await Task.Delay(TimeSpan.FromSeconds(3));

        responseProxy.ShouldContinueWithIngressAsNormal = true;
    };
});
```


## How to use for Egress Throttling

### To configure via appsettings.json

1. Add the following `ThrottlingTrollEgress` section to your config file:
```
  "ThrottlingTrollEgress": {

    "Rules": [
        ... here go rate limiting rules and limits...
    ],

    "WhiteList": [
        ... here go whitelisted URIs...
    ]
    
  },

```

2. Use the following code to configure a named HttpClient at startup:
```
builder.Services.AddHttpClient("my-throttled-httpclient").AddThrottlingTrollMessageHandler();
```

3. Get an instance of that HttpClient via IHttpClientFactory:
```
var throttledHttpClient = this._httpClientFactory.CreateClient("my-throttled-httpclient");
```

### To configure programmatically

Create an HttpClient instance like this:
```
var myThrottledHttpClient = new HttpClient
(
    new ThrottlingTrollHandler
    (
        new ThrottlingTrollEgressConfig
        {
            Rules = new[]
            {
                new ThrottlingTrollRule
                {
                    UriPattern = "/some/external/url",
                    LimitMethod = new SlidingWindowRateLimitMethod
                    {
                        PermitLimit = 5,
                        IntervalInSeconds = 10,
                        NumOfBuckets = 5
                    }
                },
            }
        }
    )
);
```

NOTE: normally HttpClient instances should be created once and reused.

### To configure dynamically

1. Use the following code to configure a named HttpClient at startup:
```
builder.Services.AddHttpClient("my-throttled-httpclient").AddThrottlingTrollMessageHandler(options =>
{
    options.GetConfigFunc = async () =>
    {
        var myThrottlingRules = await LoadThrottlingRulesFromDatabase();

        return new ThrottlingTrollConfig
        {
            Rules = myThrottlingRules
        };
    };

    options.IntervalToReloadConfigInSeconds = 10;
});
```

2. Get an instance of that HttpClient via [IHttpClientFactory](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/http-requests?view=aspnetcore-7.0#named-clients):
```
var throttledHttpClient = this._httpClientFactory.CreateClient("my-throttled-httpclient");
```

### To propagate from Egress to Ingress

If your service internally makes HTTP requests and you want to automatically propagate `429 TooManyRequests` responses up to your service's clients, configure your ThrottlingTroll-enabled HttpClient with `PropagateToIngress` property set to `true`:

```
  "ThrottlingTrollEgress": {

    "PropagateToIngress": true
  }
```

This will make [ThrottlingTrollHandler](https://github.com/scale-tone/ThrottlingTroll/blob/main/ThrottlingTroll/ThrottlingTrollHandler.cs) throw a dedicated [ThrottlingTrollTooManyRequestsException](https://github.com/scale-tone/ThrottlingTroll/blob/main/ThrottlingTroll/ThrottlingTrollTooManyRequestsException.cs), which then will be handled by [ThrottlingTrollMiddleware](https://github.com/scale-tone/ThrottlingTroll/blob/main/ThrottlingTroll/ThrottlingTrollMiddleware.cs). The `Retry-After` header value (if present) will also be propagated.


### To use with [RestSharp](https://restsharp.dev/)

Configure RestClient as follows:
```
var restClientOptions = new RestClientOptions("https://contoso.com/api/values")
{
    ConfigureMessageHandler = unused =>

        new ThrottlingTrollHandler
        (
            new ThrottlingTrollEgressConfig
            {
                Rules = new []
                {
                    new ThrottlingTrollRule
                    {
                        LimitMethod = new FixedWindowRateLimitMethod
                        {
                            PermitLimit = 3,
                            IntervalInSeconds = 10,
                        }
                    }
                }

            }
        )
};

var restClient = new RestClient(restClientOptions);
```

and then use it as normal.


### To make HttpClient do automatic retries when getting 429 TooManyRequests

Provide a response fabric implementation via **ResponseFabric** option and set **ShouldRetryEgressRequest** to **true** in it:
```
builder.Services.AddHttpClient("my-retrying-httpclient").AddThrottlingTrollMessageHandler(options =>
{
    options.ResponseFabric = async (limitExceededResult, requestProxy, responseProxy, cancelToken) =>
    {
        // Doing no more than 10 automatic retries
        responseProxy.ShouldRetryEgressRequest = responseProxy.EgressResponseRetryCount < 10;
    };
});
```

HttpClient will then first wait for the amount of time suggested by **Retry-After** response header and then re-send the request. This will happen no matter whether `429 TooManyRequests` status was returned by the external resource or it was the egress rate limit that got exceeded.

## Supported Rate Counter Stores

By default ThrottlingTroll will store rate counters in memory, using [MemoryCacheCounterStore](https://github.com/scale-tone/ThrottlingTroll/blob/main/ThrottlingTroll/CounterStores/MemoryCacheCounterStore.cs) (which internally uses [System.Runtime.Caching.MemoryCache](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.caching.memorycache)).

Other supported options are:

* [DistributedCacheCounterStore](https://github.com/scale-tone/ThrottlingTroll/blob/main/ThrottlingTroll/CounterStores/DistributedCacheCounterStore.cs). Uses [IDistributedCache](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-7.0#idistributedcache-interface) instance taken from DI container. 
* [RedisCounterStore](https://github.com/scale-tone/ThrottlingTroll/blob/main/ThrottlingTroll/CounterStores/RedisCounterStore.cs). Specifically designed to work with Redis. Prefer this one over [DistributedCacheCounterStore](https://github.com/scale-tone/ThrottlingTroll/blob/main/ThrottlingTroll/CounterStores/DistributedCacheCounterStore.cs) + [Distributed Redis Cache](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-7.0#distributed-redis-cache).

You can also create your custom Counter Store by implementing the [ICounterStore](https://github.com/scale-tone/ThrottlingTroll/blob/main/ThrottlingTroll/CounterStores/ICounterStore.cs) interface.

### How to specify a Rate Counter Store to be used

Either put a desired [ICounterStore](https://github.com/scale-tone/ThrottlingTroll/blob/main/ThrottlingTroll/CounterStores/ICounterStore.cs) implementation into DI container:
```
builder.Services.AddSingleton<ICounterStore>(
    provider => new DistributedCacheCounterStore(provider.GetRequiredService<IDistributedCache>())
);
```

Or provide it via **UseThrottlingTroll()** method:
```
app.UseThrottlingTroll(options =>
{
    options.CounterStore = new RedisCounterStore(app.Services.GetRequiredService<IConnectionMultiplexer>());
});
```

## Samples

[Here is a sample project, that demonstrates all the above concepts](https://github.com/scale-tone/ThrottlingTroll/tree/main/samples/ThrottlingTrollSampleWeb).

## Contributing

Is very much welcomed.
