using Microsoft.Extensions.Primitives;
using System.Collections.Generic;

namespace ThrottlingTroll
{
    /// <summary>
    /// Abstraction layer for all supported HTTP requests
    /// </summary>
    public interface IHttpRequestProxy
    {
        /// <summary>
        /// Request URI
        /// </summary>
        public string Uri { get; }

        /// <summary>
        /// Request URI without query string
        /// </summary>
        public string UriWithoutQueryString { get; }

        /// <summary>
        /// Request HTTP method
        /// </summary>
        public string Method { get; }

        /// <summary>
        /// Request HTTP Headers
        /// </summary>
        public IDictionary<string, StringValues> Headers { get; }

        /// <summary>
        /// Appends a list of values to a named entry in the request's context.Items.
        /// If an entry does not exist yet, it is created out of the provided list.
        /// Otherwise the values are appended to the existing list.
        /// </summary>
        public void AppendToContextItem<T>(string key, List<T> list);

        /// <summary>
        /// Request context key-value storage
        /// </summary>
        public IDictionary<object, object> RequestContextItems { get; }
    }
}