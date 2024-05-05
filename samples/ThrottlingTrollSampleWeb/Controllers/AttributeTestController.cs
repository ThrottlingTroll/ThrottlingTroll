using Microsoft.AspNetCore.Mvc;
using ThrottlingTroll;

namespace ThrottlingTrollSampleWeb.Controllers
{
    /// <summary>
    /// Demonstrates how to apply <see cref="ThrottlingTrollAttribute"/>s to an API controller.
    /// All methods have a shared limit of 20 requests per 20 seconds.
    /// </summary>
    [Route("/my-[controller]-api")]
    [ThrottlingTroll(PermitLimit = 20, IntervalInSeconds = 20, ResponseBody = "Controller-level limit exceeded. Retry in 20 seconds.")]
    public class AttributeTestController : ControllerBase
    {
        /// <summary>
        /// Matches controller's route and therefore controller's limit.
        /// </summary>
        [HttpGet]
        public string Test1()
        {
            return "OK";
        }

        /// <summary>
        /// Action-level limit - 2 requests per 2 seconds.
        /// Endpoint has multiple routes.
        /// </summary>
        [ThrottlingTroll(PermitLimit = 2, IntervalInSeconds = 2, ResponseBody = "my-test-endpoint2 limit exceeded. Retry in 2 seconds.")]
        [HttpGet("my-test-endpoint2")]
        [HttpGet("/test-endpoint-of-mine2")]
        public string Test2()
        {
            return "OK";
        }

        /// <summary>
        /// Endpoint with a parameter.
        /// Action-level limit - 3 requests per 3 seconds
        /// </summary>
        [HttpGet("my-{num}th-endpoint")]
        [ThrottlingTroll(PermitLimit = 3, IntervalInSeconds = 3, ResponseBody = "my-nth-endpoint limit exceeded. Retry in 3 seconds.")]
        public string Test3(int num)
        {
            return num.ToString();
        }

        /// <summary>
        /// Action-level limit - 4 requests per 4 seconds applied to POST and PUT requests, 2 requests per 4 seconds applied to PATCH requests.
        /// Controller-level limit still applies to all requests.
        /// Demonstrates how to apply multiple limits to the same endpoint. 
        /// </summary>
        [ThrottlingTroll(PermitLimit = 4, IntervalInSeconds = 4, Method = "POST,PUT", ResponseBody = "Limit for POST and PUT requests exceeded. Retry in 4 seconds.")]
        [ThrottlingTroll(PermitLimit = 2, IntervalInSeconds = 4, Method = "PATCH", ResponseBody = "Limit for PATCH requests exceeded. Retry in 4 seconds.")]
        [Route("my-test-endpoint4")]
        [HttpGet]
        [HttpPost]
        [HttpPut]
        [HttpPatch]
        public string Test4()
        {
            return "OK";
        }

        /// <summary>
        /// Action-level limit - 5 requests per 5 seconds.
        /// Demonstrates how to explicitly specify <see cref="ThrottlingTrollAttribute.UriPattern"/>, if ThrottlingTroll fails to infer it correctly.
        /// </summary>
        [ThrottlingTroll(UriPattern = "test-endpoint5", PermitLimit = 5, IntervalInSeconds = 5, ResponseBody = "my-test-endpoint5 limit exceeded. Retry in 5 seconds.")]
        [HttpGet("my-test-endpoint5")]
        public string Test5()
        {
            return "OK";
        }

        /// <summary>
        /// Action-level limit - 6 requests per 6 seconds.
        /// Route contains special characters.
        /// </summary>
        [ThrottlingTroll(PermitLimit = 6, IntervalInSeconds = 6, ResponseBody = "~/(m-y)/test$endpoint+6 limit exceeded. Retry in 6 seconds.")]
        [HttpGet("~/(m-y)/test$endpoint+6")]
        public string Test6()
        {
            return "OK";
        }

        /// <summary>
        /// Action-level limit - 7 requests per 7 seconds.
        /// Route starts with tilde
        /// </summary>
        [ThrottlingTroll(PermitLimit = 7, IntervalInSeconds = 7, ResponseBody = "~/my/test/endpoint/7 limit exceeded. Retry in 7 seconds.")]
        [HttpGet("~/my/test/endpoint/7")]
        public string Test7()
        {
            return "OK";
        }
    }
}