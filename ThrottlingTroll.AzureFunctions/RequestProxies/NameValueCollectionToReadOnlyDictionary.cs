using Microsoft.Extensions.Primitives;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace ThrottlingTroll
{
    /// <summary>
    /// Converts <see cref="NameValueCollectionToReadOnlyDictionary"/> to <see cref="IReadOnlyDictionary{string, StringValues}"/>
    /// </summary>
    internal class NameValueCollectionToReadOnlyDictionary : IReadOnlyDictionary<string, StringValues>
    {
        internal NameValueCollectionToReadOnlyDictionary(NameValueCollection query)
        {
            this._query = query;
        }

        /// <inheritdoc />
        public StringValues this[string key] => new StringValues(this._query.GetValues(key));

        /// <inheritdoc />
        public IEnumerable<string> Keys => this._query.AllKeys;

        /// <inheritdoc />
        public IEnumerable<StringValues> Values => 
            this._query.AllKeys.Select(k => new StringValues(this._query[k]));

        /// <inheritdoc />
        public int Count => this._query.Count;

        /// <inheritdoc />
        public bool ContainsKey(string key) => this._query.GetValues(key) != null;

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator() =>
            this.Keys.Select(k => new KeyValuePair<string, StringValues>(k, this[k]))
            .GetEnumerator();

        /// <inheritdoc />
        public bool TryGetValue(string key, [MaybeNullWhen(false)] out StringValues value)
        {
            var values = this._query.GetValues(key);
            if (values == null)
            {
                value = default;
                return false;
            }
            else
            {
                value = new StringValues(values);
                return true;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => this._query.GetEnumerator();

        private readonly NameValueCollection _query;
    }
}