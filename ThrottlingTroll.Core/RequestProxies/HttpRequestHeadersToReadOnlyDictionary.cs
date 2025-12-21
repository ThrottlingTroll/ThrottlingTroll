using Microsoft.Extensions.Primitives;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http.Headers;

namespace ThrottlingTroll
{
    /// <summary>
    /// Converts <see cref="HttpRequestHeaders"/> to <see cref="IReadOnlyDictionary{string, StringValues}"/>
    /// </summary>
    internal class HttpRequestHeadersToReadOnlyDictionary : IReadOnlyDictionary<string, StringValues>
    {
        internal HttpRequestHeadersToReadOnlyDictionary(HttpRequestHeaders headers)
        {
            this._headers = headers;
        }

        /// <inheritdoc />
        public StringValues this[string key] => new StringValues(this._headers.GetValues(key).ToArray());

        /// <inheritdoc />
        public IEnumerable<string> Keys => this._headers.Select(kv => kv.Key);

        /// <inheritdoc />
        public IEnumerable<StringValues> Values => this._headers.Select(kv => new StringValues(kv.Value.ToArray()));

        /// <inheritdoc />
        public int Count => this._headers.Count();

        /// <inheritdoc />
        public bool ContainsKey(string key) => this._headers.Contains(key);

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator() => 
            this._headers.Select(kv => new KeyValuePair<string, StringValues>(kv.Key, new StringValues(kv.Value.ToArray())))
            .GetEnumerator();

        /// <inheritdoc />
        public bool TryGetValue(string key, [MaybeNullWhen(false)] out StringValues value)
        {
            if (this._headers.TryGetValues(key, out var values))
            {
                value = new StringValues(values.ToArray());
                return true;
            }

            value = default;
            return false;
        }

        IEnumerator IEnumerable.GetEnumerator() => this._headers.GetEnumerator();

        private readonly HttpRequestHeaders _headers;
    }
}