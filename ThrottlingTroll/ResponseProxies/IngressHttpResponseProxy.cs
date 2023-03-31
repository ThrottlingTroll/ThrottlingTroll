using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace ThrottlingTroll
{
    /// <summary>
    /// Abstraction layer on top of <see cref="HttpResponse"/>
    /// </summary>
    public class IngressHttpResponseProxy : IHttpResponseProxy
    {
        internal IngressHttpResponseProxy(HttpResponse response)
        {
            this.Response = response;
        }

        /// <summary>
        /// Ingress <see cref="HttpResponse"/>
        /// </summary>
        public HttpResponse Response { get; private set; }

        /// <inheritdoc />
        public int StatusCode
        {
            get
            {
                return this.Response.StatusCode;
            }
            set
            {
                this.Response.StatusCode = value;
            }
        }

        /// <inheritdoc />
        public void SetHttpHeader(string headerName, string headerValue)
        {
            this.Response.Headers.Remove(headerName);
            this.Response.Headers.Add(headerName, headerValue);
        }

        /// <inheritdoc />
        public Task WriteAsync(string text)
        {
            return this.Response.WriteAsync(text);
        }

        /// <summary>
        /// Set this to true, if you want ThrottlingTroll to continue processing ingress request as normal 
        /// (instead of returning 429 TooManyRequests).
        /// </summary>
        public bool ShouldContinueAsNormal { get; set; }
    }
}