using Microsoft.AspNetCore.Http;

namespace ThrottlingTroll
{
    /// <summary>
    /// Abstraction layer on top of <see cref="HttpRequest"/>
    /// </summary>
    public interface IIncomingHttpRequestProxy : IHttpRequestProxy
    {
        /// <summary>
        /// Incoming <see cref="HttpRequest"/>.
        /// </summary>
        public HttpRequest Request { get; }
    }
}