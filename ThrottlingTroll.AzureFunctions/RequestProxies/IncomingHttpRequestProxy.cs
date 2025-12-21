using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Primitives;
using System.Collections.Generic;

namespace ThrottlingTroll
{
    /// <summary>
    /// Abstraction layer on top of <see cref="HttpRequest"/>
    /// </summary>
    public class IncomingHttpRequestProxy : IIncomingHttpRequestProxy
    {
        internal IncomingHttpRequestProxy(FunctionContext functionContext)
        {
            this._functionContext = functionContext;
            this.Request = functionContext.GetHttpContext().Request;
            this.Headers = new HeaderDictionaryToReadOnlyDictionary(this.Request.Headers);
            this.Query = new QueryCollectionToReadOnlyDictionary(this.Request.Query);
        }

        /// <inheritdoc />
        public HttpRequest Request { get; private set; }

        /// <inheritdoc />
        public string Uri
        {
            get
            {
                string path = this.Request.Path.ToString().Trim('/');
                if (!string.IsNullOrEmpty(path))
                {
                    path = "/" + path;
                }

                string url = $"{this.Request.Scheme}://{this.Request.Host}{path}{this.Request.QueryString}";

                return url;
            }
        }

        /// <inheritdoc />
        public string UriWithoutQueryString => $"{this.Request.Scheme}://{this.Request.Host}{this.Request.Path}";

        /// <inheritdoc />
        public string Method => this.Request.Method;

        /// <inheritdoc />
        public IReadOnlyDictionary<string, StringValues> Headers { get; private set; }

        public IReadOnlyDictionary<string, StringValues> Query { get; private set; }

        /// <inheritdoc />
        public void AppendToContextItem<T>(string key, List<T> list)
        {
            // Adding both to FunctionContext and HttpContext
            this._functionContext.Items.AddItemsToKey(key, list);
            this.Request.HttpContext.Items[key] = this._functionContext.Items[key];
        }

        /// <inheritdoc />
        public IDictionary<object, object> RequestContextItems => this.Request.HttpContext.Items;

        private readonly FunctionContext _functionContext;
    }
}