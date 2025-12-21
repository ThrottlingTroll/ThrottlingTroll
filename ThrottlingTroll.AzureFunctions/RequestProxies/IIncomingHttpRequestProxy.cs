using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker.Http;

namespace ThrottlingTroll
{
    /// <summary>
    /// Abstraction layer on top of <see cref="HttpRequestData"/>
    /// </summary>
    public interface IIncomingHttpRequestProxy : IHttpRequestProxy
    {
        /// <summary>
        /// Incoming <see cref="HttpRequestData"/>.
        /// May be null, if it is ASP.Net Core Integration.
        /// </summary>
        public HttpRequestData RequestData { get; }

        /// <summary>
        /// Incoming <see cref="HttpRequest"/>.
        /// May be null, if it is "classic" Azure Function.
        /// </summary>
        public HttpRequest Request { get; }
    }
}