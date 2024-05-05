using System;

namespace ThrottlingTroll
{
    /// <summary>
    /// Rate limit to be applied to this particular controller or method
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class ThrottlingTrollAttribute : Attribute, IRateLimitMethodSettings
    {
        /// <inheritdoc />
        public RateLimitAlgorithm Algorithm { get; set; }

        /// <inheritdoc />
        public int PermitLimit { get; set; }

        /// <inheritdoc />
        public int IntervalInSeconds { get; set; }

        /// <inheritdoc />
        public int NumOfBuckets { get; set; }

        /// <inheritdoc />
        public int TimeoutInSeconds { get; set; } = 100;

        /// <inheritdoc />
        public bool? ShouldThrowOnFailures { get; set; }

        /// <summary>
        /// A Regex pattern to match request URI against.
        /// Use this property to explicitly specify the pattern, if ThrottlingTroll is unable to automatically infer it correctly.
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
        /// Setting this to something more than 0 makes ThrottlingTroll wait until the counter drops below the limit,
        /// but no longer than MaxDelayInSeconds. Use this setting to implement delayed responses or critical sections.
        /// </summary>
        public int MaxDelayInSeconds { get; set; } = 0;

        /// <summary>
        /// Response body to be sent, when a limit is exceeded.
        /// Overrides <see cref="ThrottlingTrollOptions.ResponseFabric"/>
        /// </summary>
        public string ResponseBody { get; set; }

        /// <summary>
        /// HTTP status code to be sent, when a limit is exceeded.
        /// Overrides <see cref="ThrottlingTrollOptions.ResponseFabric"/>
        /// </summary>
        public int ResponseStatusCode { get; set; } = 0;

        /// <summary>
        /// Creates a <see cref="ThrottlingTrollRule"/> out of this attribute
        /// </summary>
        public ThrottlingTrollRule ToThrottlingTrollRule(string uriPattern, string httpMethods = null)
        {
            var rule = new ThrottlingTrollRule
            {
                LimitMethod = this.ToRateLimitMethod(),
                UriPattern = string.IsNullOrEmpty(this.UriPattern) ?  uriPattern : this.UriPattern,
                Method = string.IsNullOrEmpty(this.Method) ? httpMethods : this.Method,
                HeaderName = this.HeaderName,
                HeaderValue = this.HeaderValue,
                IdentityId = this.IdentityId,
                MaxDelayInSeconds = this.MaxDelayInSeconds
            };

            if (!string.IsNullOrEmpty(this.ResponseBody) || this.ResponseStatusCode != 0) 
            {
                rule.ResponseFabric = (checkResults, requestProxy, responseProxy, requestAborted) =>
                {
                    responseProxy.StatusCode = this.ResponseStatusCode == 0 ? 429 : this.ResponseStatusCode;

                    return responseProxy.WriteAsync(string.IsNullOrEmpty(this.ResponseBody) ? "Too many requests" : this.ResponseBody);
                };
            }

            return rule;
        }
    }
}
