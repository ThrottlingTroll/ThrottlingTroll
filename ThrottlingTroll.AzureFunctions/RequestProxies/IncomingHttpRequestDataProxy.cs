using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Primitives;
using System.Collections.Generic;

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
            this.Headers = new HttpHeadersCollectionToReadOnlyDictionary(request.Headers);
        }

        /// <inheritdoc />
        public HttpRequestData RequestData { get; private set; }

        /// <inheritdoc />
        public string Uri => this.RequestData.Url?.ToString();

        /// <inheritdoc />
        public string UriWithoutQueryString => $"{this.RequestData.Url?.Scheme}://{this.RequestData.Url?.Authority}{this.RequestData.Url?.AbsolutePath}";

        /// <inheritdoc />
        public string Method => this.RequestData.Method;

        /// <inheritdoc />
        public IReadOnlyDictionary<string, StringValues> Headers { get; private set; }

        /// <inheritdoc />
        public IReadOnlyDictionary<string, StringValues> Query { get; private set; }

        /// <inheritdoc />
        public void AppendToContextItem<T>(string key, List<T> list)
        {
            this.RequestData.FunctionContext.Items.AddItemsToKey(key, list);
        }

        /// <inheritdoc />
        public IDictionary<object, object> RequestContextItems => this.RequestData.FunctionContext.Items;
    }
}