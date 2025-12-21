using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace ThrottlingTroll
{
    /// <summary>
    /// Converts <see cref="IHeaderDictionary"/> to <see cref="IReadOnlyDictionary{string, StringValues}"/>
    /// </summary>
    internal class HeaderDictionaryToReadOnlyDictionary : IReadOnlyDictionary<string, StringValues>
    {
        internal HeaderDictionaryToReadOnlyDictionary(IHeaderDictionary headers)
        {
            this._headers = headers;
        }

        /// <inheritdoc />
        public StringValues this[string key] => this._headers[key];

        /// <inheritdoc />
        public IEnumerable<string> Keys => this._headers.Keys;

        /// <inheritdoc />
        public IEnumerable<StringValues> Values => this._headers.Select(kv => kv.Value);

        /// <inheritdoc />
        public int Count => this._headers.Count;

        /// <inheritdoc />
        public bool ContainsKey(string key) => this._headers.ContainsKey(key);

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator() => this._headers.GetEnumerator();

        /// <inheritdoc />
        public bool TryGetValue(string key, [MaybeNullWhen(false)] out StringValues value) => this._headers.TryGetValue(key, out value);

        IEnumerator IEnumerable.GetEnumerator() => this._headers.GetEnumerator();

        private readonly IHeaderDictionary _headers;
    }
}