using System.Net.Http;
using Microsoft.AspNetCore.Http;

namespace ThrottlingTroll
{
    /// <summary>
    /// Unifies <see cref="HttpResponse"/> (inbound requests) and <see cref="HttpResponseMessage"/> (outbound requests)
    /// </summary>
    public class HttpResponseProxy
    {
        internal HttpResponseProxy(HttpResponse response)
        {
            this.IngressResponse = response;
        }

        internal HttpResponseProxy(HttpResponseMessage responseMessage, int retryCount)
        {
            this.EgressResponse = responseMessage;
            this.EgressResponseRetryCount = retryCount;
        }

        /// <summary>
        /// Ingress <see cref="HttpResponse"/>
        /// </summary>
        public HttpResponse IngressResponse { get; private set; }

        /// <summary>
        /// Egress <see cref="HttpResponseMessage"/>
        /// </summary>
        public HttpResponseMessage EgressResponse { get; private set; }

        /// <summary>
        /// Set this to true, if you want ThrottlingTroll to automatically retry the outgoing request.
        /// </summary>
        public bool ShouldRetryEgressRequest { get; set; }

        /// <summary>
        /// How many times the current outgoing request was retried so far. Use this counter to control <see cref="ShouldRetryEgressRequest"/> flag.
        /// </summary>
        public int EgressResponseRetryCount { get; private set; }
    }
}