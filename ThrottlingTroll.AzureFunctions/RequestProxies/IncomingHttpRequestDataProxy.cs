using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Primitives;
using System.Collections.Generic;
using System.Linq;

namespace ThrottlingTroll
{
    /// <summary>
    /// Abstraction layer on top of <see cref="HttpRequestData"/>
    /// </summary>
    public class IncomingHttpRequestDataProxy : IIncomingHttpRequestDataProxy
    {
        internal IncomingHttpRequestDataProxy(HttpRequestData request)
        {
            this.RequestData = request;
        }

        /// <inheritdoc />
        public HttpRequestData RequestData { get; private set; }

        /// <inheritdoc />
        public string Uri
        {
            get
            {
                return this.RequestData.Url?.ToString();
            }
        }

        /// <inheritdoc />
        public string UriWithoutQueryString
        {
            get
            {
                return $"{this.RequestData.Url?.Scheme}://{this.RequestData.Url?.Authority}{this.RequestData.Url?.AbsolutePath}";
            }
        }

        /// <inheritdoc />
        public string Method
        {
            get
            {
                return this.RequestData.Method;
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

                    foreach (var header in this.RequestData.Headers)
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
            this.RequestData.FunctionContext.Items.AddItemsToKey(key, list);
        }

        /// <inheritdoc />
        public IDictionary<object, object> RequestContextItems
        {
            get
            {
                return this.RequestData.FunctionContext.Items;
            }
        }

        private IDictionary<string, StringValues> _headers;
    }
}