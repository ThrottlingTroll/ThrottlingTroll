using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Extensions.Primitives;

namespace ThrottlingTroll
{
    /// <summary>
    /// Abstraction layer on top of <see cref="HttpRequestMessage"/>
    /// </summary>
    public class OutgoingHttpRequestProxy : IOutgoingHttpRequestProxy
    {
        internal OutgoingHttpRequestProxy(HttpRequestMessage request)
        {
            this.Request = request;
            this.Headers = new HttpRequestHeadersToReadOnlyDictionary(request.Headers);
        }

        /// <inheritdoc />
        public HttpRequestMessage Request { get; private set; }

        /// <inheritdoc />
        public string Uri => this.Request.RequestUri?.ToString();

        /// <inheritdoc />
        public string UriWithoutQueryString => $"{this.Request.RequestUri?.Scheme}://{this.Request.RequestUri?.Authority}{this.Request.RequestUri?.AbsolutePath}";

        /// <inheritdoc />
        public string Method => this.Request.Method.Method;

        /// <inheritdoc />
        public IReadOnlyDictionary<string, StringValues> Headers { get; private set; }

        /// <inheritdoc />
        public IReadOnlyDictionary<string, StringValues> Query => throw new NotImplementedException();

        /// <inheritdoc />
        public void AppendToContextItem<T>(string key, List<T> list)
        {
            // Doing nothing so far
        }

        /// <inheritdoc />
        public IDictionary<object, object> RequestContextItems { get; private set; } = new Dictionary<object, object>();
    }
}