using System.Net.Http;

namespace ThrottlingTroll
{
    /// <summary>
    /// Abstraction layer on top of <see cref="HttpResponseMessage"/>
    /// </summary>
    public interface IEgressHttpResponseProxy : IHttpResponseProxy
    {
        /// <summary>
        /// Egress <see cref="HttpResponseMessage"/>
        /// </summary>
        public HttpResponseMessage Response { get; }

        /// <summary>
        /// Set this to true, if you want ThrottlingTroll to automatically retry the outgoing request.
        /// </summary>
        public bool ShouldRetry { get; set; }

        /// <summary>
        /// How many times the current outgoing request was retried so far. Use this counter to control <see cref="ShouldRetry"/> flag.
        /// </summary>
        public int RetryCount { get; }
    }
}