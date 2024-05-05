using Microsoft.AspNetCore.Mvc;
using RestSharp;
using StackExchange.Redis;
using ThrottlingTroll;

namespace ThrottlingTrollSampleWeb.Controllers
{
    [ApiController]
    public class TestController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConnectionMultiplexer _redis;

        public TestController(IHttpClientFactory httpClientFactory, IConnectionMultiplexer redis = null)
        {
            this._httpClientFactory = httpClientFactory;
            this._redis = redis;
        }

        /// <summary>
        /// Rate limited to 3 requests per a fixed window of 10 seconds. Configured via appsettings.json.
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="429">TooManyRequests</response>
        [HttpGet]
        [Route("fixed-window-3-requests-per-10-seconds-configured-via-appsettings")]
        public string Test1()
        {
            // Here is how to set a custom header with the number of remaining requests
            // Obtaining the current list of limit check results from HttpContext.Items
            var limitCheckResults = this.HttpContext.GetThrottlingTrollLimitCheckResults();

            // Now finding the minimal RequestsRemaining number (since there can be multiple rules matched)
            var minRequestsRemaining = limitCheckResults.OrderByDescending(r => r.RequestsRemaining).FirstOrDefault();
            if (minRequestsRemaining != null)
            {
                // Now setting the custom header
                this.Response.Headers.Add("X-Requests-Remaining", minRequestsRemaining.RequestsRemaining.ToString());
            }

            return "OK";
        }

        /// <summary>
        /// Rate limited to 5 requests per a sliding window of 15 seconds split into 5 buckets. Configured via appsettings.json.
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="429">TooManyRequests</response>
        [HttpGet]
        [Route("sliding-window-5-requests-per-15-seconds-with-5-buckets-configured-via-appsettings")]
        public string Test2()
        {
            return "OK";
        }

        /// <summary>
        /// Rate limited to 1 request per a fixed window of 2 seconds. Configured programmatically.
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="429">TooManyRequests</response>
        [HttpGet]
        [Route("fixed-window-1-request-per-2-seconds-configured-programmatically")]
        public string Test3()
        {
            return "OK";
        }

        /// <summary>
        /// Rate limited to 7 requests per a sliding window of 20 seconds split into 4 buckets. Configured dynamically (via a callback, that's being re-executed periodically).
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="429">TooManyRequests</response>
        [HttpGet]
        [Route("sliding-window-7-requests-per-20-seconds-with-4-buckets-configured-dynamically")]
        public string Test4()
        {
            return "OK";
        }

        /// <summary>
        /// Rate limited to 3 requests per a fixed window of 15 seconds per each identity.
        /// Query string's 'api-key' parameter is used as identityId.
        /// Demonstrates how to use identity extractors.
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="429">TooManyRequests</response>
        [HttpGet]
        [Route("fixed-window-3-requests-per-15-seconds-per-each-api-key")]
        public string Test5([FromQuery(Name = "api-key")] string apiKey)
        {
            return "OK";
        }

        /// <summary>
        /// Rate limited to 1 request per a fixed window of 2 seconds.
        /// Custom throttled response is returned, with 400 BadRequest status code and custom body.
        /// Demonstrates how to use custom response fabrics.
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="400">BadRequest</response>
        [HttpGet]
        [Route("fixed-window-1-request-per-2-seconds-response-fabric")]
        public string Test6()
        {
            return "OK";
        }

        /// <summary>
        /// Rate limited to 1 request per a fixed window of 2 seconds.
        /// Throttled response is delayed for 3 seconds (instead of returning an error).
        /// Demonstrates how to implement a delay with a custom response fabric.
        /// </summary>
        /// <response code="200">OK</response>
        [HttpGet]
        [Route("fixed-window-1-request-per-2-seconds-delayed-response")]
        public string Test7()
        {
            return "OK";
        }

        /// <summary>
        /// Rate limited to 2 concurrent requests.
        /// Demonstrates Semaphore (Concurrency) rate limiter.
        /// DON'T TEST IT IN BROWSER, because browsers themselves limit the number of concurrent requests to the same URL.
        /// </summary>
        /// <response code="200">OK</response>
        [HttpGet]
        [Route("semaphore-2-concurrent-requests")]
        public async Task<string> Test8()
        {
            await Task.Delay(TimeSpan.FromSeconds(10));

            return "OK";
        }

        /// <summary>
        /// Rate limited to 1 concurrent request per each identity, other requests are delayed.
        /// Demonstrates how to make a named distributed critical section with Semaphore (Concurrency) rate limiter and Identity Extractor.
        /// Query string's 'id' parameter is used as identityId.
        /// DON'T TEST IT IN BROWSER, because browsers themselves limit the number of concurrent requests to the same URL.
        /// </summary>
        /// <response code="200">OK</response>
        [HttpGet]
        [Route("named-critical-section")]
        public async Task<string> Test9()
        {
            await Task.Delay(TimeSpan.FromSeconds(10));

            return "OK";
        }

        private static Dictionary<string, long> Counters = new Dictionary<string, long>();

        /// <summary>
        /// Endpoint for testing Semaphores. Increments a counter value, but NOT atomically.
        /// </summary>
        /// <response code="200">OK</response>
        [HttpGet]
        [Route("distributed-counter")]
        public async Task<long> Test10([FromQuery] string? id)
        {
            long counter = 1;
            string counterId = $"TestCounter{id}";

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

            return counter;
        }

        /// <summary>
        /// A balance of 10 coins per a fixed window of 20 seconds. Cost of a request is taken from 'cost' query string parameter. 
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="429">TooManyRequests</response>
        [HttpGet]
        [Route("fixed-window-balance-of-10-per-20-seconds")]
        public string Test11()
        {
            return "OK";
        }

        /// <summary>
        /// Uses a rate-limited HttpClient to make calls to a dummy endpoint. Rate limited to 2 requests per a fixed window of 5 seconds.
        /// </summary>
        /// <response code="200">OK</response>
        [HttpGet]
        [Route("egress-fixed-window-2-requests-per-5-seconds-configured-via-appsettings")]
        public async Task<string> EgressTest1()
        {
            using var client = this._httpClientFactory.CreateClient("my-throttled-httpclient");

            string url = $"{this.Request.Scheme}://{this.Request.Host}/dummy";

            var response = await client.GetAsync(url);

            return $"Dummy endpoint returned {response.StatusCode}";
        }

        /// <summary>
        /// Calls /fixed-window-3-requests-per-10-seconds-configured-via-appsettings endpoint 
        /// using an HttpClient that is configured to propagate 429 responses.  
        /// HttpClient configured in-place programmatically.
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="429">TooManyRequests</response>
        [HttpGet]
        [Route("egress-fixed-window-3-requests-per-10-seconds-configured-programmatically")]
        public async Task<string> EgressTest2()
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

            string url = $"{this.Request.Scheme}://{this.Request.Host}/fixed-window-3-requests-per-10-seconds-configured-via-appsettings";

            await client.GetAsync(url);

            return "OK";
        }

        /// <summary>
        /// Uses a rate-limited <see href="https://restsharp.dev">RestSharp</see>'s RestClient to make calls to a dummy endpoint. 
        /// Rate limited to 3 requests per a fixed window of 10 seconds.
        /// </summary>
        /// <response code="200">OK</response>
        [HttpGet]
        [Route("egress-fixed-window-3-requests-per-10-seconds-via-restsharp")]
        public async Task<string> EgressTest3()
        {
            string url = $"{this.Request.Scheme}://{this.Request.Host}/dummy";

            var restClientOptions = new RestClientOptions(url)
            {
                ConfigureMessageHandler = unused =>

                    new ThrottlingTrollHandler
                    (
                        new ThrottlingTrollEgressConfig
                        {
                            Rules = new[]
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

            // NOTE: RestClient instances should normally be reused. Here we're creating separate instances only for the sake of simplicity.
            using var restClient = new RestClient(restClientOptions);

            try
            {
                var res = await restClient.GetAsync(new RestRequest());
            }
            catch (HttpRequestException ex)
            {
                return $"Dummy endpoint returned {ex.StatusCode}";
            }

            return $"Dummy endpoint returned OK";
        }

        /// <summary>
        /// Calls /fixed-window-3-requests-per-10-seconds-configured-via-appsettings endpoint 
        /// using an HttpClient that is configured to do retries.
        /// </summary>
        /// <response code="200">OK</response>
        [HttpGet]
        [Route("egress-fixed-window-3-requests-per-10-seconds-with-retries")]
        public async Task<string> EgressTest4()
        {
            using var client = this._httpClientFactory.CreateClient("my-retrying-httpclient");

            string url = $"{this.Request.Scheme}://{this.Request.Host}/fixed-window-3-requests-per-10-seconds-configured-via-appsettings";

            var response = await client.GetAsync(url);

            return $"Dummy endpoint returned {response.StatusCode}";
        }


        /// <summary>
        /// Calls /dummy endpoint 
        /// using an HttpClient that is limited to 3 requests per 5 seconds and does automatic delays and retries.
        /// HttpClient configured in-place programmatically.
        /// </summary>
        /// <response code="200">OK</response>
        [HttpGet]
        [Route("egress-fixed-window-3-requests-per-5-seconds-with-delays")]
        public async Task<string> EgressTest5()
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

            string url = $"{this.Request.Scheme}://{this.Request.Host}/dummy";

            await client.GetAsync(url);

            return "OK";
        }

        /// <summary>
        /// Calls /lazy-dummy endpoint 
        /// using an HttpClient that is limited to 2 concurrent requests.
        /// Demonstrates Semaphore (Concurrency) rate limiter.
        /// DON'T TEST IT IN BROWSER, because browsers themselves limit the number of concurrent requests to the same URL.
        /// </summary>
        /// <response code="200">OK</response>
        [HttpGet]
        [Route("egress-semaphore-2-concurrent-requests")]
        public async Task<string> EgressTest6()
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

            string url = $"{this.Request.Scheme}://{this.Request.Host}/lazy-dummy";

            var response = await client.GetAsync(url);

            return $"Dummy endpoint returned {response.StatusCode}";
        }

        /// <summary>
        /// Dummy endpoint for testing HttpClient. Isn't throttled.
        /// </summary>
        /// <response code="200">OK</response>
        [HttpGet]
        [Route("dummy")]
        public string Dummy()
        {
            return "OK";
        }

        /// <summary>
        /// Dummy endpoint for testing HttpClient. Sleeps for 10 seconds. Isn't throttled.
        /// </summary>
        /// <response code="200">OK</response>
        [HttpGet]
        [Route("lazy-dummy")]
        public async Task<string> LazyDummy()
        {
            await Task.Delay(TimeSpan.FromSeconds(10));

            return "OK";
        }

        /// <summary>
        /// Dumps all the current effective ThrottlingTroll configuration for debugging purposes.
        /// Never do this in a real service.
        /// </summary>
        [HttpGet]
        [Route("throttling-troll-config-debug-dump")]
        public List<ThrottlingTrollConfig> ThrottlingTrollConfigDebugDump()
        {
            // ThrottlingTroll places a list of ThrottlingTrollConfigs into request's context under the "ThrottlingTrollConfigsContextKey" key
            // The value is a list, because there might be multiple instances of ThrottlingTrollMiddleware configured
            return this.HttpContext.GetThrottlingTrollConfig();
        }
    }
}