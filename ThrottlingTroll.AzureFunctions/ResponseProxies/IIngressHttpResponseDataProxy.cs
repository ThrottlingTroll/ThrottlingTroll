using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker.Http;

namespace ThrottlingTroll
{
    /// <summary>
    /// Abstraction layer on top of <see cref="HttpResponseData"/>
    /// </summary>
    public interface IIngressHttpResponseDataProxy : IIngressHttpResponseProxyBase
    {
        /// <summary>
        /// Ingress <see cref="HttpResponseData"/>
        /// </summary>
        public HttpResponseData ResponseData { get; }
    }
}