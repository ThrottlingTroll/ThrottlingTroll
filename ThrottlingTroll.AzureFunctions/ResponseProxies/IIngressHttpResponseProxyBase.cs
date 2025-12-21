
namespace ThrottlingTroll
{
    /// <summary>
    /// Abstraction layer for ingress HTTP responses
    /// </summary>
    public interface IIngressHttpResponseProxyBase : IHttpResponseProxy
    {
        /// <summary>
        /// Set this to true, if you want ThrottlingTroll to continue processing ingress request as normal 
        /// (instead of returning 429 TooManyRequests).
        /// </summary>
        public bool ShouldContinueAsNormal { get; set; }
    }
}