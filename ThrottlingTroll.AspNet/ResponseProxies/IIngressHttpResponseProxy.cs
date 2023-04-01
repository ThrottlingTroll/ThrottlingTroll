
using Microsoft.AspNetCore.Http;

namespace ThrottlingTroll
{
    /// <summary>
    /// Abstraction layer on top of <see cref="HttpResponse"/>
    /// </summary>
    public interface IIngressHttpResponseProxy : IHttpResponseProxy
    {
        /// <summary>
        /// Ingress <see cref="HttpResponse"/>
        /// </summary>
        public HttpResponse Response { get; }

        /// <summary>
        /// Set this to true, if you want ThrottlingTroll to continue processing ingress request as normal 
        /// (instead of returning 429 TooManyRequests).
        /// </summary>
        public bool ShouldContinueAsNormal { get; set; }
    }
}