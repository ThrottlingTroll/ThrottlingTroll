using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker.Http;

namespace ThrottlingTroll
{
    /// <summary>
    /// Abstraction layer on top of <see cref="HttpResponseData"/>
    /// </summary>
    public class IngressHttpResponseProxy : IIngressHttpResponseProxy
    {
        internal IngressHttpResponseProxy(HttpResponseData response)
        {
            this.Response = response;
        }

        /// <inheritdoc />
        public HttpResponseData Response { get; private set; }

        /// <inheritdoc />
        public int StatusCode
        {
            get
            {
                return (int)this.Response.StatusCode;
            }
            set
            {
                this.Response.StatusCode = (HttpStatusCode)value;
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
            return this.Response.WriteStringAsync(text);
        }

        /// <inheritdoc />
        public bool ShouldContinueAsNormal { get; set; }
    }
}