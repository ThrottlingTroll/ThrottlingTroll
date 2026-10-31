using System.Diagnostics;

namespace IntegrationTests
{
    /// <summary>
    /// Test helpers
    /// </summary>
    public static class Helpers
    {
        /// <summary>
        /// Adds a given traceId as a traceparent header
        /// </summary>
        public static HttpRequestMessage WithTraceId(this HttpRequestMessage request, string traceId)
        {
            request.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom().ToHexString()}-01");
            return request;
        }

        /// <summary>
        /// Executes a GET request with traceId added as a traceparent header
        /// </summary>
        public static async Task<HttpResponseMessage> GetWithTraceIdAsync(this HttpClient client, string requestUri, string traceId)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri)
                .WithTraceId(traceId);

            return await client.SendAsync(request);
        }
    }
}
