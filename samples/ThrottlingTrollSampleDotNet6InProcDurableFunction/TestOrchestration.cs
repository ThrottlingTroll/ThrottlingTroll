using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using ThrottlingTroll;
using ThrottlingTroll.CounterStores.AzureTable;
using ThrottlingTroll.CounterStores.Redis;

namespace ThrottlingTrollSampleDotNet6InProcDurableFunction
{
    public static class TestOrchestration
    {
        [FunctionName("TestOrchestration")]
        public static async Task<string[]> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var tasks = new List<Task<string>>();

            // Replace "hello" with the name of your Durable Activity Function.
            tasks.Add(context.CallActivityAsync<string>(nameof(SayHello), "Tokyo"));
            tasks.Add(context.CallActivityAsync<string>(nameof(SayHello), "Seattle"));
            tasks.Add(context.CallActivityAsync<string>(nameof(SayHello), "London"));
            tasks.Add(context.CallActivityAsync<string>(nameof(SayHello), "Oslo"));
            tasks.Add(context.CallActivityAsync<string>(nameof(SayHello), "Reykjavik"));

            // returns ["Hello Tokyo!", "Hello Seattle!", "Hello London!"]
            return await Task.WhenAll(tasks);
        }

        /// <summary>
        /// This HttpClient instance will limit itself to a specified number of requests per second.
        /// When the limit is exceeded, the actual HTTP call will NOT be made until the counter falls back below the limit.
        /// Note that in this configuration, if the API constantly returns 429, the client will do retries infinitely.
        /// Consider using egressResponse.ShouldRetry = egressResponse.RetryCount < 10 instead.
        /// </summary>
        private static HttpClient ThrottledHttpClient = new HttpClient
        (
            new ThrottlingTrollHandler
            (
                async (limitExceededResult, httpRequestProxy, httpResponseProxy, cancellationToken) =>
                {
                    var egressResponse = (IEgressHttpResponseProxy)httpResponseProxy;
                    egressResponse.ShouldRetry = true;
                },

                // If RedisConnectionString is specified, then using RedisCounterStore, otherwise using AzureTableCounterStore
                counterStore: 
                    string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RedisConnectionString")) ?
                    new AzureTableCounterStore() : 
                    new RedisCounterStore(ConnectionMultiplexer.Connect(Environment.GetEnvironmentVariable("RedisConnectionString"))),

                // One request per each 5 seconds
                new ThrottlingTrollEgressConfig
                {
                    Rules = new[]
                    {
                        new ThrottlingTrollRule
                        {
                            LimitMethod = new FixedWindowRateLimitMethod
                            {
                                PermitLimit = 1,
                                IntervalInSeconds = 5
                            }
                        }
                    }
                }
            )
        );

        [FunctionName(nameof(SayHello))]
        public static async Task<string> SayHello([ActivityTrigger] string name, ILogger log)
        {
            // Calling our own API via ThrottledHttpClient
            string hostName = Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME");

            var response = await ThrottledHttpClient.GetAsync($"{(hostName.StartsWith("localhost") ? "http://" : $"https://")}{hostName}/api/httphello?name={name}");

            return await response.Content.ReadAsStringAsync();
        }

        [FunctionName("TestOrchestration_HttpStart")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            // Function input comes from the request content.
            string instanceId = await starter.StartNewAsync("TestOrchestration", null);

            log.LogInformation("Started orchestration with ID = '{instanceId}'.", instanceId);

            return starter.CreateCheckStatusResponse(req, instanceId);
        }
    }
}