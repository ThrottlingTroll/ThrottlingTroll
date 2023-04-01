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
    }
}