using System.Net.Http;

namespace ThrottlingTroll
{
    /// <summary>
    /// Abstraction layer on top of <see cref="HttpRequestMessage"/>
    /// </summary>
    public interface IOutgoingHttpRequestProxy : IHttpRequestProxy
    {
        /// <summary>
        /// Outgoing <see cref="HttpRequestMessage"/>
        /// </summary>
        public HttpRequestMessage Request { get; }
    }
}