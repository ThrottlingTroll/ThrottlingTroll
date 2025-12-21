using Microsoft.Extensions.Primitives;
using System.Collections.Generic;

namespace ThrottlingTroll
{
    /// <summary>
    /// Generic (dummy) implementation of <see cref="IHttpRequestProxy"/>, to be used for throttling any method calls.
    /// </summary>
    public class GenericRequestProxy : IHttpRequestProxy
    {
        /// <inheritdoc />
        public string Uri { get; set; }

        /// <inheritdoc />
        public string UriWithoutQueryString { get; set; }

        /// <inheritdoc />
        public string Method { get; set; }

        /// <inheritdoc />
        public IReadOnlyDictionary<string, StringValues> Headers { get; set; } = new Dictionary<string, StringValues>();

        /// <inheritdoc />
        public IReadOnlyDictionary<string, StringValues> Query { get; set; } = new Dictionary<string, StringValues>();

        /// <inheritdoc />
        public IDictionary<object, object> RequestContextItems { get; set; }

        /// <inheritdoc />
        public void AppendToContextItem<T>(string key, List<T> list)
        {
        }
    }
}