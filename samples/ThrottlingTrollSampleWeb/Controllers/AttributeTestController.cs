using Microsoft.AspNetCore.Mvc;
using ThrottlingTroll;

namespace ThrottlingTrollSampleWeb.Controllers
{
    /// <summary>
    /// Demonstrates how to apply <see cref="ThrottlingTrollAttribute"/>s to an API controller
    /// </summary>
    [ApiController]
    [Route("/my-[controller]-api")]
    [ThrottlingTroll(PermitLimit = 20, IntervalInSeconds = 20, ResponseBody = "Controller-level limit exceeded. Retry in 20 seconds.")]
    public class AttributeTestController : Controller
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
        /// Action-level limit - 2 requests per 2 seconds
        /// </summary>
        [ThrottlingTroll(PermitLimit = 2, IntervalInSeconds = 2, ResponseBody = "my-test-endpoint2 limit exceeded. Retry in 2 seconds.")]
        [HttpGet("my-test-endpoint2")]
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
        /// Action-level limit - 4 requests per 4 seconds, only applied to POST and PUT requests.
        /// Controller-level limit still applies to all requests.
        /// </summary>
        [ThrottlingTroll(PermitLimit = 4, IntervalInSeconds = 4, Method = "POST,PUT", ResponseBody = "my-test-endpoint4 limit exceeded. Retry in 4 seconds.")]
        [Route("my-test-endpoint4")]
        [HttpGet]
        [HttpPost]
        [HttpPut]
        public string Test4()
        {
            return "OK";
        }

        /// <summary>
        /// Action-level limit - 5 requests per 5 seconds
        /// Demonstrates how to explicitly specify <see cref="ThrottlingTrollAttribute.UriPattern"/>, if ThrottlingTroll fails to infer it correctly.
        /// </summary>
        [ThrottlingTroll(UriPattern = "test-endpoint5", PermitLimit = 5, IntervalInSeconds = 5, ResponseBody = "my-test-endpoint5 limit exceeded. Retry in 5 seconds.")]
        [HttpGet("my-test-endpoint5")]
        public string Test5()
        {
            return "OK";
        }
    }
}