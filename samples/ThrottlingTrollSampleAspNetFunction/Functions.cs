using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using ThrottlingTroll;

namespace ThrottlingTrollSampleAspNetFunction
{
    public class Functions
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConnectionMultiplexer? _redis;

        public Functions(IHttpClientFactory httpClientFactory, IConnectionMultiplexer? redis = null)
        {
            this._httpClientFactory = httpClientFactory;
            this._redis = redis;
        }

        /// <summary>
        /// Rate limited to 3 requests per a fixed window of 10 seconds. Configured via appsettings.json.
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="429">TooManyRequests</response>
        [Function("fixed-window-3-requests-per-10-seconds-configured-via-appsettings")]
        public IActionResult Test1([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
        {
            return new OkObjectResult("OK");
        }

        /// <summary>
        /// Rate limited to 5 requests per a sliding window of 15 seconds split into 5 buckets. Configured via appsettings.json.
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="429">TooManyRequests</response>
        [Function("sliding-window-5-requests-per-15-seconds-with-5-buckets-configured-via-appsettings")]
        public IActionResult Test2([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
        {
            return new OkObjectResult("OK");
        }

        /// <summary>
        /// Rate limited to 1 request per a fixed window of 2 seconds. Configured programmatically.
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="429">TooManyRequests</response>
        [Function("fixed-window-1-request-per-2-seconds-configured-programmatically")]
        public IActionResult Test3([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
        {
            return new OkObjectResult("OK");
        }

        /// <summary>
        /// Rate limited to 7 requests per a sliding window of 20 seconds split into 4 buckets. Configured dynamically (via a callback, that's being re-executed periodically).
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="429">TooManyRequests</response>
        [Function("sliding-window-7-requests-per-20-seconds-with-4-buckets-configured-dynamically")]
        public IActionResult Test4([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
        {
            return new OkObjectResult("OK");
        }

        /// <summary>
        /// Rate limited to 3 requests per a fixed window of 15 seconds per each identity.
        /// Query string's 'api-key' parameter is used as identityId.
        /// Demonstrates how to use identity extractors.
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="429">TooManyRequests</response>
        [Function("fixed-window-3-requests-per-15-seconds-per-each-api-key")]
        public IActionResult Test5([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
        {
            return new OkObjectResult("OK");
        }

        /// <summary>
        /// Rate limited to 1 request per a fixed window of 2 seconds.
        /// Custom throttled response is returned, with 400 BadRequest status code and custom body.
        /// Demonstrates how to use custom response fabrics.
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="400">BadRequest</response>
        [Function("fixed-window-1-request-per-2-seconds-response-fabric")]
        public IActionResult Test6([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
        {
            return new OkObjectResult("OK");
        }

        /// <summary>
        /// Rate limited to 1 request per a fixed window of 2 seconds.
        /// Throttled response is delayed for 3 seconds (instead of returning an error).
        /// Demonstrates how to implement a delay with a custom response fabric.
        /// </summary>
        /// <response code="200">OK</response>
        [Function("fixed-window-1-request-per-2-seconds-delayed-response")]
        public IActionResult Test7([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
        {
            return new OkObjectResult("OK");
        }

        /// <summary>
        /// Rate limited to 2 concurrent requests.
        /// Demonstrates Semaphore (Concurrency) rate limiter.
        /// DON'T TEST IT IN BROWSER, because browsers themselves limit the number of concurrent requests to the same URL.
        /// </summary>
        /// <response code="200">OK</response>
        [Function("semaphore-2-concurrent-requests")]
        public async Task<IActionResult> Test8([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
        {
            await Task.Delay(TimeSpan.FromSeconds(10));

            return new OkObjectResult("OK");
        }

        /// <summary>
        /// Rate limited to 1 concurrent request per each identity, other requests are delayed.
        /// Demonstrates how to make a named distributed critical section with Semaphore (Concurrency) rate limiter and Identity Extractor.
        /// Query string's 'id' parameter is used as identityId.
        /// DON'T TEST IT IN BROWSER, because browsers themselves limit the number of concurrent requests to the same URL.
        /// </summary>
        /// <response code="200">OK</response>
        [Function("named-critical-section")]
        public async Task<IActionResult> Test9([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
        {
            await Task.Delay(TimeSpan.FromSeconds(10));

            return new OkObjectResult("OK");
        }

        private static Dictionary<string, long> Counters = new Dictionary<string, long>();

        /// <summary>
        /// Endpoint for testing Semaphores. Increments a counter value, but NOT atomically.
        /// </summary>
        /// <response code="200">OK</response>
        [Function("distributed-counter")]
        public async Task<IActionResult> Test10([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
        {
            long counter = 1;
            string counterId = $"TestCounter{req.Query["id"]}";

            // The below code is intentionally not thread-safe

            if (this._redis != null)
            {
                var db = this._redis.GetDatabase();

                counter = (long)await db.StringGetAsync(counterId);

                counter++;

                await db.StringSetAsync(counterId, counter);
            }
            else
            {
                if (Counters.ContainsKey(counterId))
                {
                    counter = Counters[counterId];

                    counter++;
                }

                Counters[counterId] = counter;
            }

            return new OkObjectResult(counter.ToString());
        }

        /// <summary>
        /// Uses a rate-limited HttpClient to make calls to a dummy endpoint. Rate limited to 2 requests per a fixed window of 5 seconds.
        /// </summary>
        /// <response code="200">OK</response>
        [Function("egress-fixed-window-2-requests-per-5-seconds-configured-via-appsettings")]
        public async Task<IActionResult> EgressTest1([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
        {
            using var client = this._httpClientFactory.CreateClient("my-throttled-httpclient");

            // Print debug line with all headers.
            var headers = req.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());

            // Get hostname and port for the current function. This is needed to construct a URL for the dummy endpoint.           
            string url = $"{req.Scheme}://{GetHostAndPort(req)}/api/dummy";

            var clientResponse = await client.GetAsync(url);

            return new OkObjectResult($"Dummy endpoint returned {clientResponse.StatusCode}");
        }

        /// <summary>
        /// Calls /fixed-window-3-requests-per-10-seconds-configured-via-appsettings endpoint 
        /// using an HttpClient that is configured to propagate 429 responses.  
        /// HttpClient configured in-place programmatically.
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="429">TooManyRequests</response>
        [Function("egress-fixed-window-3-requests-per-10-seconds-configured-programmatically")]
        public async Task<IActionResult> EgressTest2([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
        {
            // NOTE: HttpClient instances should normally be reused. Here we're creating separate instances only for the sake of simplicity.
            using var client = new HttpClient
            (
                new ThrottlingTrollHandler
                (
                    new ThrottlingTrollEgressConfig
                    {
                        PropagateToIngress = true
                    }
                )
            );

            string url = $"{req.Scheme}://{GetHostAndPort(req)}/api/fixed-window-3-requests-per-10-seconds-configured-via-appsettings";

            await client.GetAsync(url);

            return new OkObjectResult("OK");
        }

        /// <summary>
        /// Calls /fixed-window-3-requests-per-10-seconds-configured-via-appsettings endpoint 
        /// using an HttpClient that is configured to do retries.
        /// </summary>
        /// <response code="200">OK</response>
        [Function("egress-fixed-window-3-requests-per-10-seconds-with-retries")]
        public async Task<IActionResult> EgressTest4([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
        {
            using var client = this._httpClientFactory.CreateClient("my-retrying-httpclient");

            string url = $"{req.Scheme}://{GetHostAndPort(req)}/api/fixed-window-3-requests-per-10-seconds-configured-via-appsettings";

            var clientResponse = await client.GetAsync(url);

            return new OkObjectResult($"Dummy endpoint returned {clientResponse.StatusCode}");
        }

        /// <summary>
        /// Calls /dummy endpoint 
        /// using an HttpClient that is limited to 3 requests per 5 seconds and does automatic delays and retries.
        /// HttpClient configured in-place programmatically.
        /// </summary>
        /// <response code="200">OK</response>
        [Function("egress-fixed-window-3-requests-per-5-seconds-with-delays")]
        public async Task<IActionResult> EgressTest5([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
        {
            // NOTE: HttpClient instances should normally be reused. Here we're creating separate instances only for the sake of simplicity.
            using var client = new HttpClient
            (
                new ThrottlingTrollHandler
                (
                    async (limitExceededResult, httpRequestProxy, httpResponseProxy, cancellationToken) =>
                    {
                        var egressResponse = (IEgressHttpResponseProxy)httpResponseProxy;

                        egressResponse.ShouldRetry = true;
                    },

                    counterStore: null,

                    new ThrottlingTrollEgressConfig
                    {
                        Rules = new[]
                        {
                            new ThrottlingTrollRule
                            {
                                LimitMethod = new FixedWindowRateLimitMethod
                                {
                                    PermitLimit = 3,
                                    IntervalInSeconds = 5
                                }
                            }
                        }
                    }
                )
            );

            string url = $"{req.Scheme}://{GetHostAndPort(req)}/api/dummy";

            await client.GetAsync(url);

            return new OkObjectResult("OK");
        }

        /// <summary>
        /// Calls /lazy-dummy endpoint 
        /// using an HttpClient that is limited to 2 concurrent requests.
        /// Demonstrates Semaphore (Concurrency) rate limiter.
        /// DON'T TEST IT IN BROWSER, because browsers themselves limit the number of concurrent requests to the same URL.
        /// </summary>
        /// <response code="200">OK</response>
        [Function("egress-semaphore-2-concurrent-requests")]
        public async Task<IActionResult> EgressTest6([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
        {
            // NOTE: HttpClient instances should normally be reused. Here we're creating separate instances only for the sake of simplicity.
            using var client = new HttpClient
            (
                new ThrottlingTrollHandler
                (
                    new ThrottlingTrollEgressConfig
                    {
                        Rules = new[]
                        {
                            new ThrottlingTrollRule
                            {
                                LimitMethod = new SemaphoreRateLimitMethod
                                {
                                    PermitLimit = 2
                                }
                            }
                        }
                    }
                )
            );

            string url = $"{req.Scheme}://{GetHostAndPort(req)}/api/lazy-dummy";

            var clientResponse = await client.GetAsync(url);

            return new OkObjectResult($"Dummy endpoint returned {clientResponse.StatusCode}");
        }

        /// <summary>
        /// Dummy endpoint for testing HttpClient. Isn't throttled.
        /// </summary>
        /// <response code="200">OK</response>
        [Function("dummy")]
        public IActionResult Dummy([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
        {
            return new OkObjectResult("OK");
        }

        /// <summary>
        /// Dummy endpoint for testing HttpClient. Sleeps for 10 seconds. Isn't throttled.
        /// </summary>
        /// <response code="200">OK</response>
        [Function("lazy-dummy")]
        public async Task<IActionResult> LazyDummy([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
        {
            await Task.Delay(TimeSpan.FromSeconds(10));

            return new OkObjectResult("OK");
        }

        /// <summary>
        /// Dumps all the current effective ThrottlingTroll configuration for debugging purposes.
        /// Never do this in a real service.
        /// </summary>
        [Function("throttling-troll-config-debug-dump")]
        public IActionResult ThrottlingTrollConfigDebugDump([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
        {
            // ThrottlingTroll places a list of ThrottlingTrollConfigs into request's context under the "ThrottlingTrollConfigsContextKey" key
            // The value is a list, because there might be multiple instances of ThrottlingTrollMiddleware configured
            var configList = (List<ThrottlingTrollConfig>)req.HttpContext.Items[ThrottlingTroll.ThrottlingTroll.ThrottlingTrollConfigsContextKey]!;

            return new OkObjectResult(configList);
        }

        private string GetHostAndPort(HttpRequest req)
        {
            // Use the X-Forwarded-Host header if present, otherwise use the Host header.
            if (req.Headers.TryGetValue("X-Forwarded-Host", out var hostAndPort))
            {
                return hostAndPort;
            }
            else
            {
                return req.Host.Value;
            }
        }
    }
}
