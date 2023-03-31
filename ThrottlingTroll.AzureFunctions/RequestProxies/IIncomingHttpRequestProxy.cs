using Microsoft.Azure.Functions.Worker.Http;

namespace ThrottlingTroll
{
    /// <summary>
    /// Abstraction layer on top of <see cref="HttpRequestData"/>
    /// </summary>
    public interface IIncomingHttpRequestProxy : IHttpRequestProxy
    {
        /// <summary>
        /// Incoming <see cref="HttpRequestData"/>
        /// </summary>
        public HttpRequestData Request { get; }
    }
}