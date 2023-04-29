using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using System;

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
        /// Request's HTTP method. E.g. "POST". Empty string or null means any method.
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
        /// </summary>
        public Func<IHttpRequestProxy, string> IdentityIdExtractor { get; set; }

        /// <summary>
        /// Checks whether given request matches this filter
        /// </summary>
        internal bool IsMatch(IHttpRequestProxy request)
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
        /// Constructs a cache key for the limit counter, based on this filter's values.
        /// If <see cref="IdentityIdExtractor"/> is set, applies it as well.
        /// </summary>
        protected string GetUniqueCacheKey(IHttpRequestProxy request, string configName)
        {
            if (this.IdentityIdExtractor == null)
            {
                // Our key is static, so calculating its hash only once for optimization purposes
                if (string.IsNullOrEmpty(this._cacheKey))
                {
                    string key = $"<{this.Method}>|<{this.UriPattern}>|<{this.HeaderName}>|<{this.HeaderValue}>";

                    this._cacheKey = this.GetHash(key);
                }

                return $"{configName}|{this._cacheKey}";
            }
            else
            {
                // If IdentityExtractor is set, then adding request's identityId to the cache key,
                // so that different identities get different counters.

                string identityId = this.IdentityIdExtractor(request);

                string key = $"<{this.Method}>|<{this.UriPattern}>|<{this.HeaderName}>|<{this.HeaderValue}>|<{identityId}>";

                return $"{configName}|{this.GetHash(key)}";
            }
        }

        private bool IsUrlMatch(IHttpRequestProxy request)
        {
            if (this.UrlRegex == null)
            {
                return true;
            }

            return this.UrlRegex.IsMatch(request.Uri);
        }

        private bool IsMethodMatch(IHttpRequestProxy request)
        {
            if (string.IsNullOrEmpty(this.Method))
            {
                return true;
            }

            return request.Method.ToLower() == this.Method.ToLower();
        }

        private bool IsHeaderMatch(IHttpRequestProxy request)
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

        private bool IsIdentityMatch(IHttpRequestProxy request)
        {
            if (this.IdentityIdExtractor == null || string.IsNullOrEmpty(this.IdentityId))
            {
                return true;
            }

            var identityId = this.IdentityIdExtractor(request);

            return identityId == this.IdentityId;
        }

        private string GetHash(string str)
        {
            // HashAlgorithm instances should NOT be reused
            using (var sha256 = SHA256.Create())
            {
                var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(str));

                return Convert.ToBase64String(bytes);
            }
        }

        private Regex _uriRegex;
        private string _cacheKey;
    }
}