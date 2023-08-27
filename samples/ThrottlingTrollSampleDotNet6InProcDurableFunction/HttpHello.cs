using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ThrottlingTrollSampleDotNet6InProcDurableFunction
{
    public static class HttpHello
    {
        [FunctionName("HttpHello")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req, ILogger log)
        {
            string name = req.Query["name"];

            log.LogInformation("Saying hello to {name}.", name);

            return new OkObjectResult($"Hello {name}!");
        }
    }
}
