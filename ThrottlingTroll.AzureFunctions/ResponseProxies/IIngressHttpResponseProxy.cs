using Microsoft.AspNetCore.Http;

namespace ThrottlingTroll
{
    /// <summary>
    /// Abstraction layer on top of <see cref="HttpResponse"/>
    /// </summary>
    public interface IIngressHttpResponseProxy : IIngressHttpResponseProxyBase
    {
        /// <summary>
        /// Ingress <see cref="HttpResponse"/>
        /// </summary>
        public HttpResponse Response { get; }
    }
}