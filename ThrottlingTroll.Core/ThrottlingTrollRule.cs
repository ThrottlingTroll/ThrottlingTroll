using System;
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
        /// Checks if limit of calls is exceeded for a given request.
        /// If request does not match the rule, returns null.
        /// If limit exceeded, returns number of seconds to retry after and unique counter ID.
        /// Otherwise just returns unique counter ID.
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
                return new LimitExceededResult(false, this, string.Empty, uniqueCacheKey);
            }

            log(LogLevel.Warning, $"ThrottlingTroll: rule {uniqueCacheKey} exceeded by {request.Method} {request.UriWithoutQueryString}");

            return new LimitExceededResult(true, this, retryAfter.ToString(), uniqueCacheKey);
        }

        /// <summary>
        /// Will be executed at the end of a successful (not throttled) request. Used for decrementing the counter, if needed.
        /// </summary>
        internal async Task OnSuccessfulRequestProcessingFinished(ICounterStore store, string uniqueCacheKey)
        {
            await this.LimitMethod.DecrementAsync(uniqueCacheKey, store);
        }

        private RateLimitMethod _limitMethod { get; set; }
    }
}