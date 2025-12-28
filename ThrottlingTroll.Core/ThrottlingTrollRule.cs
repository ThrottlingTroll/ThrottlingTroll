using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
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
        /// This rule's name. Only used for telemetry. 
        /// Provide a meaningful name of your choice, if you want to see it in distributed traces.
        /// </summary>
        public string Name { get; set; }

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
        /// but no longer than MaxDelayInSeconds. Use this setting to implement delayed responses or critical sections.
        /// </summary>
        public int MaxDelayInSeconds { get; set; } = 0;

        /// <summary>
        /// Request's cost extraction routine. The default cost (weight) of a request is 1, but this routine allows to override that.
        /// Overrides <see cref="ThrottlingTrollOptions.CostExtractor"/>.
        /// </summary>
        [JsonConverter(typeof(ToStringJsonConverter<Func<IHttpRequestProxy, long>>))]
        public Func<IHttpRequestProxy, long> CostExtractor { get; set; }

        /// <summary>
        /// Custom response creation routine. Overrides <see cref="ThrottlingTrollOptions.ResponseFabric"/><br/>
        /// Takes <see cref="List{LimitExceededResult}"/> (represents the list of rules the request matched and the corresponding check results),<br/>
        /// <see cref="IHttpRequestProxy"/> (provides info about the ongoing request), <br/> 
        /// <see cref="IHttpResponseProxy"/> (which should be customized by your code) and <br/>
        /// <see cref="CancellationToken"/> (which indicates that the request was aborted from outside)
        /// </summary>
        [JsonConverter(typeof(ToStringJsonConverter<Func<List<LimitCheckResult>, IHttpRequestProxy, IHttpResponseProxy, CancellationToken, Task>>))]
        public Func<List<LimitCheckResult>, IHttpRequestProxy, IHttpResponseProxy, CancellationToken, Task> ResponseFabric { get; set; }

        /// <summary>
        /// Whether ThrottlingTroll's internal failures should result in exceptions or in just log entries.
        /// </summary>
        protected internal bool ShouldThrowOnFailures 
        { 
            get { return this._limitMethod?.ShouldThrowOnFailures ?? false; }
        }

        /// <summary>
        /// Whether <see cref="ThrottlingTrollConfig.AllowList"/> should be ignored when evaluating a matching request.
        /// </summary>
        public bool IgnoreAllowList 
        { 
            get { return this._limitMethod?.IgnoreAllowList ?? false; }
        }

        /// <summary>
        /// Checks if limit of calls is exceeded for a given request.
        /// If request does not match the rule, returns null.
        /// If limit exceeded, returns number of seconds to retry after and unique counter ID.
        /// Otherwise just returns unique counter ID.
        /// </summary>
        protected internal virtual async Task<LimitCheckResult> IsExceededAsync(IHttpRequestProxy request, long cost, ICounterStore store, string configName, Action<LogLevel, string> log)
        {
            if (!this.IsMatch(request) || this.LimitMethod == null)
            {
                return null;
            }
            
            string uniqueCacheKey = this.GetUniqueCacheKey(request, configName);

            var requestsRemaining = await this.LimitMethod.IsExceededAsync(uniqueCacheKey, cost, store, request);

            if (requestsRemaining >= 0)
            {
                return new LimitCheckResult(requestsRemaining, this, 0, uniqueCacheKey);
            }

            log(LogLevel.Warning, $"ThrottlingTroll: rule {uniqueCacheKey} exceeded by {request.Method} {request.UriWithoutQueryString}");

            return new LimitCheckResult(requestsRemaining, this, this.LimitMethod.RetryAfterInSeconds, uniqueCacheKey);
        }

        /// <summary>
        /// Will be executed at the end of request processing. Used for decrementing the counter, if needed.
        /// </summary>
        protected internal virtual async Task OnRequestProcessingFinished(ICounterStore store, string uniqueCacheKey, long cost, Action<LogLevel, string> log, IHttpRequestProxy request)
        {
            try
            {
                await this.LimitMethod.DecrementAsync(uniqueCacheKey, cost, store, request);
            }
            catch (Exception ex)
            {
                log(LogLevel.Error, $"ThrottlingTroll failed. {ex}");
            }
        }

        /// <summary>
        /// Calculates given request's Cost
        /// </summary>
        protected internal virtual long GetCost(IHttpRequestProxy request)
        {
            if (this.CostExtractor == null)
            {
                return 1;
            }

            long cost = this.CostExtractor(request);

            return cost > 0 ? cost : 1;
        }

        /// <summary>
        /// Checks if the limit is still exceeded. Intended for implementing <see cref="SemaphoreRateLimitMethod"/>
        /// </summary>
        protected internal virtual Task<bool> IsStillExceededAsync(ICounterStore store, string uniqueCacheKey, IHttpRequestProxy request)
        {
            return this.LimitMethod.IsStillExceededAsync(uniqueCacheKey, store, request);
        }

        /// <summary>
        /// Constructs a cache key for the limit counter, based on this filter's values.
        /// If <see cref="RequestFilter.IdentityIdExtractor"/> is set, applies it as well.
        /// </summary>
        protected internal virtual string GetUniqueCacheKey(IHttpRequestProxy request, string configName)
        {
            // Also adding this prefix, to make sure ingress and egress rules never collide
            string ingressOrEgress = request is IOutgoingHttpRequestProxy ? "Egress" : "Ingress";

            if (this.IdentityIdExtractor == null)
            {
                // Our key is static, so calculating its hash only once for optimization purposes
                if (string.IsNullOrEmpty(this._cacheKey))
                {
                    string key = $"<{this.Method}>|<{this.UriPattern}>|<{this.HeaderName}>|<{this.HeaderValue}>|<{this._limitMethod.GetCacheKey()}>";

                    this._cacheKey = this.GetHash(key);
                }

                return $"{configName}|{ingressOrEgress}|{this._cacheKey}";
            }
            else
            {
                // If IdentityExtractor is set, then adding request's identityId to the cache key,
                // so that different identities get different counters.

                string identityId = this.IdentityIdExtractor(request);

                string key = $"<{this.Method}>|<{this.UriPattern}>|<{this.HeaderName}>|<{this.HeaderValue}>|<{this._limitMethod.GetCacheKey()}>|<{identityId}>";

                return $"{configName}|{ingressOrEgress}|{this.GetHash(key)}";
            }
        }

        /// <summary>
        /// Hashing utility, that is used for hashing counter keys. This default implementation uses <see cref="SHA256"/>.
        /// </summary>
        protected virtual string GetHash(string str)
        {
            // HashAlgorithm instances should NOT be reused
            using (var sha256 = SHA256.Create())
            {
                var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(str));

                return Convert.ToBase64String(bytes);
            }
        }

        private RateLimitMethod _limitMethod { get; set; }
        private string _cacheKey;
        private string _nameForTelemetry;

        #region Telemetry

        internal string GetNameForTelemetry()
        {
            if (this._nameForTelemetry == null)
            {
                if (string.IsNullOrEmpty(this.Name))
                {
                    string name = $"<{this.Method}>|<{this.UriPattern}>|<{this.HeaderName}>|<{this.HeaderValue}>|<{this._limitMethod?.GetCacheKey()}>";
                    this._nameForTelemetry = this.GetHash(name).Substring(0, 10);
                }
                else
                {
                    this._nameForTelemetry = this.Name;
                }
            }

            return this._nameForTelemetry;
        }

        internal void AddTagsToActivity(Activity activity)
        {
            activity?.AddTag($"{nameof(ThrottlingTrollRule)}.{nameof(base.UriPattern)}", base.UriPattern);
            activity?.AddTag($"{nameof(ThrottlingTrollRule)}.{nameof(base.Method)}", base.Method);
            activity?.AddTag($"{nameof(ThrottlingTrollRule)}.{nameof(base.HeaderName)}", base.HeaderName);
            activity?.AddTag($"{nameof(ThrottlingTrollRule)}.{nameof(base.HeaderValue)}", base.HeaderValue);
            activity?.AddTag($"{nameof(ThrottlingTrollRule)}.{nameof(base.IdentityId)}", base.IdentityId);

            activity?.AddTag($"{nameof(ThrottlingTrollRule)}.{nameof(this.MaxDelayInSeconds)}", this.MaxDelayInSeconds);
            activity?.AddTag($"{nameof(ThrottlingTrollRule)}.LimitMethod", this.LimitMethod?.GetType().Name);

            this.LimitMethod?.AddTagsToActivity(activity);
        }
        #endregion
    }
}
