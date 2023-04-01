using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace ThrottlingTroll
{
    /// <summary>
    /// Abstraction layer on top of <see cref="HttpResponse"/>
    /// </summary>
    public class IngressHttpResponseProxy : IIngressHttpResponseProxy
    {
        internal IngressHttpResponseProxy(HttpResponse response)
        {
            this.Response = response;
        }

        /// <inheritdoc />
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

        /// <inheritdoc />
        public bool ShouldContinueAsNormal { get; set; }
    }
}