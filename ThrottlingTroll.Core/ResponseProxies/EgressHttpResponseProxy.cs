﻿using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// Abstraction layer on top of <see cref="HttpResponseMessage"/>
    /// </summary>
    public class EgressHttpResponseProxy : IEgressHttpResponseProxy
    {
        internal EgressHttpResponseProxy(HttpResponseMessage responseMessage, int retryCount)
        {
            this.Response = responseMessage;
            this.RetryCount = retryCount;
        }

        /// <inheritdoc />
        public HttpResponseMessage Response { get; private set; }

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
            if (this.Response.Content != null)
            {
                this.Response.Content.Dispose();
            }

            this.Response.Content = new StringContent(text);

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public bool ShouldRetry { get; set; }

        /// <inheritdoc />
        public int RetryCount { get; private set; }
    }
}