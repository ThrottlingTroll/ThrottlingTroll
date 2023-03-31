using System.Collections.Generic;
using System.Linq;
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
        }

        /// <inheritdoc />
        public HttpRequestMessage Request { get; private set; }

        /// <inheritdoc />
        public string Uri
        {
            get
            {
                return this.Request.RequestUri?.ToString();
            }
        }

        /// <inheritdoc />
        public string UriWithoutQueryString
        {
            get
            {
                return $"{this.Request.RequestUri?.Scheme}://{this.Request.RequestUri?.Host}{this.Request.RequestUri?.AbsolutePath}";
            }
        }

        /// <inheritdoc />
        public string Method
        {
            get
            {
                return this.Request.Method.Method;
            }
        }

        /// <inheritdoc />
        public IDictionary<string, StringValues> Headers
        {
            get
            {
                if (this._headers == null)
                {
                    var headers = new Dictionary<string, StringValues>();

                    foreach (var header in this.Request.Headers)
                    {
                        headers.Add(header.Key, new StringValues(header.Value.ToArray()));
                    }

                    this._headers = headers;
                }

                return this._headers;
            }
        }

        private IDictionary<string, StringValues> _headers;
    }
}