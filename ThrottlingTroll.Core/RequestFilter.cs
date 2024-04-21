using System.Text.RegularExpressions;
using System;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace ThrottlingTroll
{
    /// <summary>
    /// Defines a Request Filter (a condition that requests must match in order to be throttled or whitelisted)
    /// </summary>
    public class RequestFilter
    {
        /// <summary>
        /// A Regex pattern to match request URI against. Empty string or null means any URI.
        /// </summary>
        public string UriPattern { get; set; }

        /// <summary>
        /// Comma-separated request's HTTP methods. E.g. "GET,POST". Empty string or null means any method.
        /// </summary>
        public string Method { get; set; }

        /// <summary>
        /// Request's HTTP header to check. If specified, the rule will only apply to requests with this header set to <see cref="HeaderValue"/>.
        /// If <see cref="HeaderName"/> is specified and <see cref="HeaderValue"/> is not - that matches requests with any value in that header.
        /// </summary>
        public string HeaderName { get; set; }

        /// <summary>
        /// Value for HTTP header identified by <see cref="HeaderName"/>. The rule will only apply to requests with that header set to this value.
        /// If <see cref="HeaderName"/> is specified and <see cref="HeaderValue"/> is not - that matches requests with any value in that header.
        /// </summary>
        public string HeaderValue { get; set; }

        /// <summary>
        /// Request's custom Identity ID. If specified, the rule will only apply to requests with this Identity ID. Identity IDs are extacted with <see cref="IdentityIdExtractor"/>.
        /// </summary>
        public string IdentityId { get; set; }

        /// <summary>
        /// Identity ID extraction routine to be used for extracting Identity IDs from requests.
        /// Overrides <see cref="ThrottlingTrollOptions.IdentityIdExtractor"/>.
        /// </summary>
        [JsonConverter(typeof(ToStringJsonConverter<Func<IHttpRequestProxy, string>>))]
        public Func<IHttpRequestProxy, string> IdentityIdExtractor { get; set; }

        protected Regex _uriRegex;

        /// <summary>
        /// Checks whether given request matches this filter
        /// </summary>
        protected internal virtual bool IsMatch(IHttpRequestProxy request)
        {
            return this.IsUrlMatch(request) &&
                this.IsMethodMatch(request) &&
                this.IsHeaderMatch(request) &&
                this.IsIdentityMatch(request);
        }

        /// <summary>
        /// <see cref="UriPattern"/> converted into <see cref="Regex"/>
        /// </summary>
        protected Regex UrlRegex
        {
            get
            {
                if (string.IsNullOrEmpty(this.UriPattern))
                {
                    return null;
                }

                if (this._uriRegex == null)
                {
                    this._uriRegex = new Regex(this.UriPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                }

                return this._uriRegex;
            }
        }

        /// <summary>
        /// Checks whether given request matches <see cref="UrlRegex"/>
        /// </summary>
        protected virtual bool IsUrlMatch(IHttpRequestProxy request)
        {
            if (this.UrlRegex == null)
            {
                return true;
            }

            return this.UrlRegex.IsMatch(request.Uri);
        }

        /// <summary>
        /// Checks whether given request matches <see cref="Method"/>
        /// </summary>
        protected virtual bool IsMethodMatch(IHttpRequestProxy request)
        {
            if (string.IsNullOrEmpty(this.Method))
            {
                return true;
            }

            return this.Method
                .ToLower()
                .Split(',')
                .Select(s => s.Trim())
                .Contains(request.Method.ToLower());
        }

        /// <summary>
        /// Checks whether given request matches <see cref="HeaderName"/>
        /// </summary>
        protected virtual bool IsHeaderMatch(IHttpRequestProxy request)
        {
            if (string.IsNullOrEmpty(this.HeaderName))
            {
                return true;
            }

            if (!request.Headers.ContainsKey(this.HeaderName))
            {
                return false;
            }

            if (string.IsNullOrEmpty(this.HeaderValue))
            {
                return true;
            }

            var headerValue = request.Headers[this.HeaderName];

            return headerValue == this.HeaderValue;
        }

        /// <summary>
        /// Checks whether given request matches <see cref="IdentityId"/>
        /// </summary>
        protected virtual bool IsIdentityMatch(IHttpRequestProxy request)
        {
            if (this.IdentityIdExtractor == null || string.IsNullOrEmpty(this.IdentityId))
            {
                return true;
            }

            var identityId = this.IdentityIdExtractor(request);

            return identityId == this.IdentityId;
        }

        /// <summary>
        /// Used to serialize delegate fields
        /// </summary>
        protected class ToStringJsonConverter<T> : JsonConverter<T>
        {
            /// <inheritdoc />
            public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                throw new NotImplementedException();
            }

            /// <inheritdoc />
            public override void Write(Utf8JsonWriter writer, T val, JsonSerializerOptions options)
            {
                writer.WriteStringValue(val.ToString());
            }
        }
    }
}
