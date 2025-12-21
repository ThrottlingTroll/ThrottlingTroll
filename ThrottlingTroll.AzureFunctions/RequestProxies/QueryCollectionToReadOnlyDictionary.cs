using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace ThrottlingTroll
{
    /// <summary>
    /// Converts <see cref="IQueryCollection"/> to <see cref="IReadOnlyDictionary{string, StringValues}"/>
    /// </summary>
    internal class QueryCollectionToReadOnlyDictionary : IReadOnlyDictionary<string, StringValues>
    {
        internal QueryCollectionToReadOnlyDictionary(IQueryCollection query)
        {
            this._query = query;
        }

        /// <inheritdoc />
        public StringValues this[string key] => this._query[key];

        /// <inheritdoc />
        public IEnumerable<string> Keys => this._query.Keys;

        /// <inheritdoc />
        public IEnumerable<StringValues> Values => this._query.Select(kv => kv.Value);

        /// <inheritdoc />
        public int Count => this._query.Count;

        /// <inheritdoc />
        public bool ContainsKey(string key) => this._query.ContainsKey(key);

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator() => this._query.GetEnumerator();

        /// <inheritdoc />
        public bool TryGetValue(string key, [MaybeNullWhen(false)] out StringValues value) => this._query.TryGetValue(key, out value);

        IEnumerator IEnumerable.GetEnumerator() => this._query.GetEnumerator();

        private readonly IQueryCollection _query;
    }
}