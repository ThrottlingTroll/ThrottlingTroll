using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ThrottlingTroll
{
    /// <summary>
    /// Defines a Rate Limiting Rule
    /// </summary>
    public class ThrottlingTrollRule
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
        public Func<HttpRequestProxy, string> IdentityIdExtractor { get; set; }

        /// <summary>
        /// Rate Limiting algorithm to use. Should be set to one of <see cref="RateLimitMethod"/>'s inheritors.
        /// </summary>
        public RateLimitMethod LimitMethod
        {
            get
            {
                if (this._limitMethod == null && this.RateLimit != null)
                {
                    // Trying to read from settings
                    this._limitMethod = this.RateLimit.ToRateLimitMethod();
                }

                return this._limitMethod;
            }
            set
            {
                this._limitMethod = value;
            }
        }

        /// <summary>
        /// Maps "RateLimit" section in config file.
        /// Needed for polymorphic deserialization.
        /// Don't use this property for programmatic configuration, use <see cref="LimitMethod"/> instead.
        /// </summary>
        public RateLimitMethodSettings RateLimit { get; set; }


        private RateLimitMethod _limitMethod { get; set; }
        private Regex _uriRegex;

        private Regex UrlRegex
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

        private string _cacheKey;

        private string CacheKey
        {
            get
            {
                if (string.IsNullOrEmpty(this._cacheKey))
                {
                    // HashAlgorithm instances should NOT be reused
                    using (var sha256 = SHA256.Create())
                    {
                        string key = $"<{this.Method}>|<{this.UriPattern}>|<{this.HeaderName}>|<{this.HeaderValue}>|<{this.IdentityId}>";

                        var keyBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));

                        this._cacheKey = Convert.ToBase64String(keyBytes);
                    }
                }

                return this._cacheKey;
            }
        }

        private bool IsMatch(HttpRequestProxy request)
        {
            return this.IsUrlMatch(request) &&
                this.IsMethodMatch(request) &&
                this.IsHeaderMatch(request) &&
                this.IsIdentityMatch(request);
        }

        private bool IsUrlMatch(HttpRequestProxy request)
        {
            if (this.UrlRegex == null)
            {
                return true;
            }

            return this.UrlRegex.IsMatch(request.Uri);
        }

        private bool IsMethodMatch(HttpRequestProxy request)
        {
            if (string.IsNullOrEmpty(this.Method))
            {
                return true;
            }

            return request.Method.ToLower() == this.Method.ToLower();
        }

        private bool IsHeaderMatch(HttpRequestProxy request)
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

        private bool IsIdentityMatch(HttpRequestProxy request)
        {
            if (this.IdentityIdExtractor == null)
            {
                return true;
            }

            var identityId = this.IdentityIdExtractor(request);

            return identityId == this.IdentityId;
        }

        /// <summary>
        /// Checks if limit of calls is exceeded for a given request.
        /// If exceeded, returns number of seconds to retry after. Otherwise returns 0.
        /// </summary>
        internal async Task<int> IsExceededAsync(HttpRequestProxy request, ICounterStore store, string configName, Action<LogLevel, string> log)
        {
            if (!this.IsMatch(request) || this.LimitMethod == null)
            {
                return 0;
            }

            string uniqueCacheKey = $"{configName}|{this.CacheKey}";

            var retryAfter = await this.LimitMethod.IsExceededAsync(uniqueCacheKey, store);

            if (retryAfter > 0)
            {
                log(LogLevel.Warning, $"ThrottlingTroll: rule {uniqueCacheKey} exceeded by {request.Method} {request.UriWithoutQueryString}");
            }

            return retryAfter;
        }
    }
}