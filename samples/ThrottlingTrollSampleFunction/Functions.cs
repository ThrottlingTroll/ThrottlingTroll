using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using ThrottlingTroll;

namespace ThrottlingTrollSampleFunction
{
    public class Functions
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public Functions(IHttpClientFactory httpClientFactory)
        {
            this._httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Rate limited to 3 requests per a fixed window of 10 seconds. Configured via appsettings.json.
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="429">TooManyRequests</response>
        [Function("fixed-window-3-requests-per-10-seconds-configured-via-appsettings")]
        public HttpResponseData Test1([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.WriteString("OK");
            return response;
        }

        /// <summary>
        /// Rate limited to 5 requests per a sliding window of 15 seconds split into 5 buckets. Configured via appsettings.json.
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="429">TooManyRequests</response>
        [Function("sliding-window-5-requests-per-15-seconds-with-5-buckets-configured-via-appsettings")]
        public HttpResponseData Test2([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.WriteString("OK");
            return response;
        }

        /// <summary>
        /// Rate limited to 1 request per a fixed window of 2 seconds. Configured programmatically.
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="429">TooManyRequests</response>
        [Function("fixed-window-1-request-per-2-seconds-configured-programmatically")]
        public HttpResponseData Test3([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.WriteString("OK");
            return response;
        }

        /// <summary>
        /// Rate limited to 7 requests per a sliding window of 20 seconds split into 4 buckets. Configured dynamically (via a callback, that's being re-executed periodically).
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="429">TooManyRequests</response>
        [Function("sliding-window-7-requests-per-20-seconds-with-4-buckets-configured-dynamically")]
        public HttpResponseData Test4([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.WriteString("OK");
            return response;
        }

        /// <summary>
        /// Rate limited to 3 requests per a fixed window of 15 seconds per each identity.
        /// Query string's 'api-key' parameter is used as identityId.
        /// Demonstrates how to use identity extractors.
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="429">TooManyRequests</response>
        [Function("fixed-window-3-requests-per-15-seconds-per-each-api-key")]
        public HttpResponseData Test5([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.WriteString("OK");
            return response;
        }

        /// <summary>
        /// Rate limited to 1 request per a fixed window of 2 seconds.
        /// Custom throttled response is returned, with 400 BadRequest status code and custom body.
        /// Demonstrates how to use custom response fabrics.
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="400">BadRequest</response>
        [Function("fixed-window-1-request-per-2-seconds-response-fabric")]
        public HttpResponseData Test6([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.WriteString("OK");
            return response;
        }

        /// <summary>
        /// Rate limited to 1 request per a fixed window of 2 seconds.
        /// Throttled response is delayed for 3 seconds (instead of returning an error).
        /// Demonstrates how to implement a delay with a custom response fabric.
        /// </summary>
        /// <response code="200">OK</response>
        [Function("fixed-window-1-request-per-2-seconds-delayed-response")]
        public HttpResponseData Test7([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.WriteString("OK");
            return response;
        }

        /// <summary>
        /// Rate limited to 2 concurrent requests.
        /// Demonstrates Semaphore (Concurrency) rate limiter.
        /// DON'T TEST IT IN BROWSER, because browsers themselves limit the number of concurrent requests to the same URL.
        /// </summary>
        /// <response code="200">OK</response>
        [Function("semaphore-2-concurrent-requests")]
        public async Task<HttpResponseData> Test8([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            await Task.Delay(TimeSpan.FromSeconds(10));

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.WriteString("OK");
            return response;
        }

        /// <summary>
        /// Uses a rate-limited HttpClient to make calls to a dummy endpoint. Rate limited to 2 requests per a fixed window of 5 seconds.
        /// </summary>
        /// <response code="200">OK</response>
        [Function("egress-fixed-window-2-requests-per-5-seconds-configured-via-appsettings")]
        public async Task<HttpResponseData> EgressTest1([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            using var client = this._httpClientFactory.CreateClient("my-throttled-httpclient");

            string url = $"{req.Url.Scheme}://{req.Url.Authority}/api/dummy";

            var clientResponse = await client.GetAsync(url);

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.WriteString($"Dummy endpoint returned {clientResponse.StatusCode}");
            return response;
        }

        /// <summary>
        /// Calls /fixed-window-3-requests-per-10-seconds-configured-via-appsettings endpoint 
        /// using an HttpClient that is configured to propagate 429 responses.  
        /// HttpClient configured in-place programmatically.
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="429">TooManyRequests</response>
        [Function("egress-fixed-window-3-requests-per-10-seconds-configured-programmatically")]
        public async Task<HttpResponseData> EgressTest2([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
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

            string url = $"{req.Url.Scheme}://{req.Url.Authority}/api/fixed-window-3-requests-per-10-seconds-configured-via-appsettings";

            await client.GetAsync(url);

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.WriteString($"OK");
            return response;
        }

        /// <summary>
        /// Calls /fixed-window-3-requests-per-10-seconds-configured-via-appsettings endpoint 
        /// using an HttpClient that is configured to do retries.
        /// </summary>
        /// <response code="200">OK</response>
        [Function("egress-fixed-window-3-requests-per-10-seconds-with-retries")]
        public async Task<HttpResponseData> EgressTest4([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            using var client = this._httpClientFactory.CreateClient("my-retrying-httpclient");

            string url = $"{req.Url.Scheme}://{req.Url.Authority}/api/fixed-window-3-requests-per-10-seconds-configured-via-appsettings";

            var clientResponse = await client.GetAsync(url);

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.WriteString($"Dummy endpoint returned {clientResponse.StatusCode}");
            return response;
        }

        /// <summary>
        /// Calls /dummy endpoint 
        /// using an HttpClient that is limited to 3 requests per 5 seconds and does automatic delays and retries.
        /// HttpClient configured in-place programmatically.
        /// </summary>
        /// <response code="200">OK</response>
        [Function("egress-fixed-window-3-requests-per-5-seconds-with-delays")]
        public async Task<HttpResponseData> EgressTest5([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
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

            string url = $"{req.Url.Scheme}://{req.Url.Authority}/api/dummy";

            await client.GetAsync(url);

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.WriteString($"OK");
            return response;
        }

        /// <summary>
        /// Calls /dummy endpoint 
        /// using an HttpClient that is limited to 3 requests per 5 seconds and does automatic delays and retries.
        /// HttpClient configured in-place programmatically.
        /// </summary>
        /// <response code="200">OK</response>
        [Function("egress-semaphore-2-concurrent-requests")]
        public async Task<HttpResponseData> EgressTest6([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
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

            string url = $"{req.Url.Scheme}://{req.Url.Authority}/api/lazy-dummy";

            var clientResponse = await client.GetAsync(url);

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.WriteString($"Dummy endpoint returned {clientResponse.StatusCode}");
            return response;
        }

        /// <summary>
        /// Dummy endpoint for testing HttpClient. Isn't throttled.
        /// </summary>
        /// <response code="200">OK</response>
        [Function("dummy")]
        public HttpResponseData Dummy([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.WriteString("OK");
            return response;
        }

        /// <summary>
        /// Dummy endpoint for testing HttpClient. Sleeps for 10 seconds. Isn't throttled.
        /// </summary>
        /// <response code="200">OK</response>
        [Function("lazy-dummy")]
        public async Task<HttpResponseData> LazyDummy([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            await Task.Delay(TimeSpan.FromSeconds(10));

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.WriteString("OK");
            return response;
        }
    }
}
