using Microsoft.Azure.Functions.Worker.Http;

namespace ThrottlingTroll
{
    /// <summary>
    /// Abstraction layer on top of <see cref="HttpRequestData"/>
    /// </summary>
    public interface IIncomingHttpRequestDataProxy : IHttpRequestProxy
    {
        /// <summary>
        /// Incoming <see cref="HttpRequestData"/>.
        /// </summary>
        public HttpRequestData RequestData { get; }
    }
}