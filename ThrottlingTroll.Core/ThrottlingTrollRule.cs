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
        public Func<IHttpRequestProxy, string> IdentityIdExtractor { get; set; }

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

        private string GetUniqueCacheKey(IHttpRequestProxy request, string configName)
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

        private string GetHash(string str)
        {
            // HashAlgorithm instances should NOT be reused
            using (var sha256 = SHA256.Create())
            {
                var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(str));

                return Convert.ToBase64String(bytes);
            }
        }

        private bool IsMatch(IHttpRequestProxy request)
        {
            return this.IsUrlMatch(request) &&
                this.IsMethodMatch(request) &&
                this.IsHeaderMatch(request) &&
                this.IsIdentityMatch(request);
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

        /// <summary>
        /// Checks if limit of calls is exceeded for a given request.
        /// If exceeded, returns number of seconds to retry after and unique counter ID. Otherwise returns null.
        /// </summary>
        internal async Task<LimitExceededResult> IsExceededAsync(IHttpRequestProxy request, ICounterStore store, string configName, Action<LogLevel, string> log)
        {
            if (!this.IsMatch(request) || this.LimitMethod == null)
            {
                return null;
            }

            string uniqueCacheKey = this.GetUniqueCacheKey(request, configName);

            var retryAfter = await this.LimitMethod.IsExceededAsync(uniqueCacheKey, store);

            if (retryAfter <= 0)
            {
                return null;
            }

            log(LogLevel.Warning, $"ThrottlingTroll: rule {uniqueCacheKey} exceeded by {request.Method} {request.UriWithoutQueryString}");

            return new LimitExceededResult(this, retryAfter.ToString(), uniqueCacheKey);
        }
    }
}