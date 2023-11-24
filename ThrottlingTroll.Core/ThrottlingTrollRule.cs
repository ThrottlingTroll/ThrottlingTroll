using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ThrottlingTroll
{
    /// <summary>
    /// Defines a Rate Limiting Rule (a combination of <see cref="RequestFilter"/> and <see cref="RateLimitMethod"/>)
    /// </summary>
    public class ThrottlingTrollRule : RequestFilter
    {
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

        /// <summary>
        /// Setting this to something more than 0 makes ThrottlingTroll wait until the counter drops below the limit,
        /// but no more than MaxDelayInSeconds. Use this setting to implement delayed responses or critical sections.
        /// </summary>
        public int MaxDelayInSeconds { get; set; } = 0;

        /// <summary>
        /// Request's cost extraction routine. The default cost (weight) of a request is 1, but this routine allows to override that.
        /// Overrides <see cref="ThrottlingTrollOptions.CostExtractor"/>.
        /// </summary>
        public Func<IHttpRequestProxy, long> CostExtractor { get; set; }

        /// <summary>
        /// Checks if limit of calls is exceeded for a given request.
        /// If request does not match the rule, returns null.
        /// If limit exceeded, returns number of seconds to retry after and unique counter ID.
        /// Otherwise just returns unique counter ID.
        /// </summary>
        internal async Task<LimitExceededResult> IsExceededAsync(IHttpRequestProxy request, long cost, ICounterStore store, string configName, Action<LogLevel, string> log)
        {
            if (!this.IsMatch(request) || this.LimitMethod == null)
            {
                return null;
            }
            
            string uniqueCacheKey = this.GetUniqueCacheKey(request, configName);

            var requestsRemaining = await this.LimitMethod.IsExceededAsync(uniqueCacheKey, cost, store);

            if (requestsRemaining >= 0)
            {
                return new LimitExceededResult(false, this, 0, uniqueCacheKey);
            }

            log(LogLevel.Warning, $"ThrottlingTroll: rule {uniqueCacheKey} exceeded by {request.Method} {request.UriWithoutQueryString}");

            return new LimitExceededResult(true, this, this.LimitMethod.RetryAfterInSeconds, uniqueCacheKey);
        }

        /// <summary>
        /// Will be executed at the end of request processing. Used for decrementing the counter, if needed.
        /// </summary>
        internal async Task OnRequestProcessingFinished(ICounterStore store, string uniqueCacheKey, long cost, Action<LogLevel, string> log)
        {
            try
            {
                await this.LimitMethod.DecrementAsync(uniqueCacheKey, cost, store);
            }
            catch (Exception ex)
            {
                log(LogLevel.Error, $"ThrottlingTroll failed. {ex}");
            }
        }

        internal long GetCost(IHttpRequestProxy request)
        {
            if (this.CostExtractor == null)
            {
                return 1;
            }

            long cost = this.CostExtractor(request);

            return cost > 0 ? cost : 1;
        }

        internal Task<bool> IsStillExceededAsync(ICounterStore store, string uniqueCacheKey)
        {
            return this.LimitMethod.IsStillExceededAsync(uniqueCacheKey, store);
        }

        private RateLimitMethod _limitMethod { get; set; }
        private string _cacheKey;

        /// <summary>
        /// Constructs a cache key for the limit counter, based on this filter's values.
        /// If <see cref="RequestFilter.IdentityIdExtractor"/> is set, applies it as well.
        /// </summary>
        private string GetUniqueCacheKey(IHttpRequestProxy request, string configName)
        {
            if (this.IdentityIdExtractor == null)
            {
                // Our key is static, so calculating its hash only once for optimization purposes
                if (string.IsNullOrEmpty(this._cacheKey))
                {
                    string key = $"<{this.Method}>|<{this.UriPattern}>|<{this.HeaderName}>|<{this.HeaderValue}>|<{this._limitMethod.GetCacheKey()}>";

                    this._cacheKey = this.GetHash(key);
                }

                return $"{configName}|{this._cacheKey}";
            }
            else
            {
                // If IdentityExtractor is set, then adding request's identityId to the cache key,
                // so that different identities get different counters.

                string identityId = this.IdentityIdExtractor(request);

                string key = $"<{this.Method}>|<{this.UriPattern}>|<{this.HeaderName}>|<{this.HeaderValue}>|<{this._limitMethod.GetCacheKey()}>|<{identityId}>";

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
    }
}