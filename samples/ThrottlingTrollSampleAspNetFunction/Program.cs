using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using ThrottlingTroll;
using ThrottlingTroll.CounterStores.Redis;

var builder = new HostBuilder();

// Need to explicitly load configuration from host.json
builder.ConfigureAppConfiguration(configBuilder => {

    configBuilder.AddJsonFile("host.json", optional: false, reloadOnChange: true);
});


builder.ConfigureServices(services => {

    // If RedisConnectionString is specified, then using RedisCounterStore.
    // Otherwise the default MemoryCacheCounterStore will be used.
    var redisConnString = Environment.GetEnvironmentVariable("RedisConnectionString");
    if (!string.IsNullOrEmpty(redisConnString))
    {
        var redis = ConnectionMultiplexer.Connect(redisConnString);

        services.AddSingleton<IConnectionMultiplexer>(redis);

        services.AddSingleton<ICounterStore>(new RedisCounterStore(redis));
    }

    // <ThrottlingTroll Egress Configuration>

    // Configuring a named HttpClient for egress throttling. Rules and limits taken from host.json
    services.AddHttpClient("my-throttled-httpclient").AddThrottlingTrollMessageHandler();

    // Configuring a named HttpClient that does automatic retries with respect to Retry-After response header
    services.AddHttpClient("my-retrying-httpclient").AddThrottlingTrollMessageHandler(options =>
    {
        options.ResponseFabric = async (checkResults, requestProxy, responseProxy, cancelToken) =>
        {
            var egressResponse = (IEgressHttpResponseProxy)responseProxy;

            egressResponse.ShouldRetry = true;
        };
    });

    // </ThrottlingTroll Egress Configuration>

});

builder.ConfigureFunctionsWebApplication((workerAppBuilder) => {
    // <ThrottlingTroll Ingress Configuration>

    workerAppBuilder.UseThrottlingTroll();

    // Static programmatic configuration
    workerAppBuilder.UseThrottlingTroll(options =>
    {
        options.Config = new ThrottlingTrollConfig
        {
            Rules = new[]
            {
                new ThrottlingTrollRule
                {
                    UriPattern = "/fixed-window-1-request-per-2-seconds-configured-programmatically",
                    LimitMethod = new FixedWindowRateLimitMethod
                    {
                        PermitLimit = 1,
                        IntervalInSeconds = 2
                    }
                }
            },

            // Specifying UniqueName is needed when multiple services store their
            // rate limit counters in the same cache instance, to prevent those services
            // from corrupting each other's counters. Otherwise you can skip it.
            UniqueName = "MyThrottledService1"
        };
    });

    // Dynamic programmatic configuration. Allows to adjust rules and limits without restarting the service.
    workerAppBuilder.UseThrottlingTroll(options =>
    {
        options.GetConfigFunc = async () =>
        {
            // Loading settings from a custom file. You can instead load them from a database
            // or from anywhere else.

            string ruleFileName = Path.Combine(AppContext.BaseDirectory, "my-dynamic-throttling-rule.json");

            string ruleJson = await File.ReadAllTextAsync(ruleFileName);

            var rule = JsonSerializer.Deserialize<ThrottlingTrollRule>(ruleJson);

            return new ThrottlingTrollConfig
            {
                Rules = new[] { rule }
            };
        };

        // The above function will be periodically called every 5 seconds
        options.IntervalToReloadConfigInSeconds = 5;
    });

    // Demonstrates how to use custom response fabrics
    workerAppBuilder.UseThrottlingTroll(options =>
    {
        options.Config = new ThrottlingTrollConfig
        {
            Rules = new[]
            {
                new ThrottlingTrollRule
                {
                    UriPattern = "/fixed-window-1-request-per-2-seconds-response-fabric",
                    LimitMethod = new FixedWindowRateLimitMethod
                    {
                        PermitLimit = 1,
                        IntervalInSeconds = 2
                    }
                }
            }
        };

        // Custom response fabric, returns 400 BadRequest + some custom content
        options.ResponseFabric = async (checkResults, requestProxy, responseProxy, requestAborted) =>
        {
            // Getting the rule that was exceeded and with the biggest RetryAfter value
            var limitExceededResult = checkResults.OrderByDescending(r => r.RetryAfterInSeconds).FirstOrDefault(r => r.RequestsRemaining < 0);
            if (limitExceededResult == null)
            {
                return;
            }

            responseProxy.StatusCode = (int)HttpStatusCode.BadRequest;

            responseProxy.SetHttpHeader("Retry-After", limitExceededResult.RetryAfterHeaderValue);

            await responseProxy.WriteAsync("Too many requests. Try again later.");
        };
    });

    // Demonstrates how to delay the response instead of returning 429
    workerAppBuilder.UseThrottlingTroll(options =>
    {
        options.Config = new ThrottlingTrollConfig
        {
            Rules = new[]
            {
                new ThrottlingTrollRule
                {
                    UriPattern = "/fixed-window-1-request-per-2-seconds-delayed-response",
                    LimitMethod = new FixedWindowRateLimitMethod
                    {
                        PermitLimit = 1,
                        IntervalInSeconds = 2
                    }
                }
            }
        };

        // Custom response fabric, impedes the normal response for 3 seconds
        options.ResponseFabric = async (checkResults, requestProxy, responseProxy, requestAborted) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));

            var ingressResponse = (IIngressHttpResponseProxy)responseProxy;
            ingressResponse.ShouldContinueAsNormal = true;
        };
    });

    // Demonstrates how to use identity extractors
    workerAppBuilder.UseThrottlingTroll(options =>
    {
        options.Config = new ThrottlingTrollConfig
        {
            Rules = new[]
            {
                new ThrottlingTrollRule
                {
                    UriPattern = "/fixed-window-3-requests-per-15-seconds-per-each-api-key",
                    LimitMethod = new FixedWindowRateLimitMethod
                    {
                        PermitLimit = 3,
                        IntervalInSeconds = 15
                    },

                    IdentityIdExtractor = request =>
                    {
                        // Identifying clients by their api-key
                        return ((IIncomingHttpRequestProxy)request).Request.Query["api-key"];
                    }
                }
            }
        };
    });

    // Demonstrates Semaphore (Concurrency) rate limiter
    // DON'T TEST IT IN BROWSER, because browsers themselves limit the number of concurrent requests to the same URL.
    workerAppBuilder.UseThrottlingTroll(options =>
    {
        options.Config = new ThrottlingTrollConfig
        {
            Rules = new[]
            {
                new ThrottlingTrollRule
                {
                    UriPattern = "/semaphore-2-concurrent-requests",
                    LimitMethod = new SemaphoreRateLimitMethod
                    {
                        PermitLimit = 2
                    }
                },
            }
        };
    });

    /// Demonstrates how to make a named distributed critical section with Semaphore (Concurrency) rate limiter and Identity Extractor.
    /// Query string's 'id' parameter is used as identityId.
    // DON'T TEST IT IN BROWSER, because browsers themselves limit the number of concurrent requests to the same URL.
    workerAppBuilder.UseThrottlingTroll(options =>
    {
        options.Config = new ThrottlingTrollConfig
        {
            Rules = new[]
            {
                new ThrottlingTrollRule
                {
                    UriPattern = "/named-critical-section",
                    LimitMethod = new SemaphoreRateLimitMethod
                    {
                        PermitLimit = 1
                    },

                    // This must be set to something > 0 for responses to be automatically delayed
                    MaxDelayInSeconds = 120,

                    IdentityIdExtractor = request =>
                    {
                        // Identifying clients by their id
                        return ((IIncomingHttpRequestProxy)request).Request.Query["id"];
                    }
                },
            }
        };
    });

    // Demonstrates how to make a distributed counter with SemaphoreRateLimitMethod
    workerAppBuilder.UseThrottlingTroll(options =>
    {
        options.Config = new ThrottlingTrollConfig
        {
            Rules = new[]
            {
                new ThrottlingTrollRule
                {
                    UriPattern = "/distributed-counter",
                    LimitMethod = new SemaphoreRateLimitMethod
                    {
                        PermitLimit = 1
                    },

                    // This must be set to something > 0 for responses to be automatically delayed
                    MaxDelayInSeconds = 120,

                    IdentityIdExtractor = request =>
                    {
                        // Identifying counters by their id
                        return ((IIncomingHttpRequestProxy)request).Request.Query["id"];
                    }
                },
            }
        };
    });


    // </ThrottlingTroll Ingress Configuration>

});



var host = builder.Build();
host.Run();
