using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Primitives;
using System.Collections.Generic;
using System.Linq;

namespace ThrottlingTroll
{
    /// <summary>
    /// Abstraction layer on top of <see cref="HttpRequestData"/>
    /// </summary>
    public class IncomingHttpRequestProxy : IIncomingHttpRequestProxy
    {
        internal IncomingHttpRequestProxy(HttpRequestData request)
        {
            this.Request = request;
        }

        /// <inheritdoc />
        public HttpRequestData Request { get; private set; }

        /// <inheritdoc />
        public string Uri
        {
            get
            {
                return this.Request.Url?.ToString();
            }
        }

        /// <inheritdoc />
        public string UriWithoutQueryString
        {
            get
            {
                return $"{this.Request.Url?.Scheme}://{this.Request.Url?.Authority}{this.Request.Url?.AbsolutePath}";
            }
        }

        /// <inheritdoc />
        public string Method
        {
            get
            {
                return this.Request.Method;
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

        /// <inheritdoc />
        public void AppendToContextItem<T>(string key, List<T> list)
        {
            this.Request.FunctionContext.Items.AddItemsToKey(key, list);
        }

        /// <inheritdoc />
        public IDictionary<object, object> RequestContextItems
        {
            get
            {
                return this.Request.FunctionContext.Items;
            }
        }

        private IDictionary<string, StringValues> _headers;
    }
}