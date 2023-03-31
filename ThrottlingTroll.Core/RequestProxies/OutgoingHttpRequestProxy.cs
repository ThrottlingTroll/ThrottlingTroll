using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.Primitives;

namespace ThrottlingTroll
{
    /// <summary>
    /// Abstraction layer on top of <see cref="HttpRequestMessage"/>
    /// </summary>
    public class OutgoingHttpRequestProxy : IHttpRequestProxy
    {
        internal OutgoingHttpRequestProxy(HttpRequestMessage request)
        {
            this.Request = request;
        }

        /// <summary>
        /// Outgoing <see cref="HttpRequestMessage"/>
        /// </summary>
        public HttpRequestMessage Request { get; private set; }

        /// <summary>
        /// Request URI
        /// </summary>
        public string Uri
        {
            get
            {
                return this.Request.RequestUri?.ToString();
            }
        }

        /// <summary>
        /// Request URI without query string
        /// </summary>
        public string UriWithoutQueryString
        {
            get
            {
                return $"{this.Request.RequestUri?.Scheme}://{this.Request.RequestUri?.Host}{this.Request.RequestUri?.AbsolutePath}";
            }
        }

        /// <summary>
        /// Request HTTP method
        /// </summary>
        public string Method
        {
            get
            {
                return this.Request.Method.Method;
            }
        }

        /// <summary>
        /// Request HTTP Headers
        /// </summary>
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