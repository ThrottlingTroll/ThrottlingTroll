using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace ThrottlingTrollSampleFunction
{
    public class Functions
    {
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
    }
}
