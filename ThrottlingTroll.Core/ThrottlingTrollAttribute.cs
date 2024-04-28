using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// Rate limit to be applied to this particular controller or method
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
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
        public Func<IHttpRequestProxy, string> IdentityIdExtractor { get; set; }

        /// <summary>
        /// Setting this to something more than 0 makes ThrottlingTroll wait until the counter drops below the limit,
        /// but no longer than MaxDelayInSeconds. Use this setting to implement delayed responses or critical sections.
        /// </summary>
        public int MaxDelayInSeconds { get; set; } = 0;

        /// <summary>
        /// Request's cost extraction routine. The default cost (weight) of a request is 1, but this routine allows to override that.
        /// Overrides <see cref="ThrottlingTrollOptions.CostExtractor"/>.
        /// </summary>
        public Func<IHttpRequestProxy, long> CostExtractor { get; set; }

        /// <summary>
        /// Custom response creation routine. Overrides <see cref="ThrottlingTrollOptions.ResponseFabric"/><br/>
        /// Takes <see cref="List{LimitExceededResult}"/> (represents the list of rules the request matched and the corresponding check results),<br/>
        /// <see cref="IHttpRequestProxy"/> (provides info about the ongoing request), <br/> 
        /// <see cref="IHttpResponseProxy"/> (which should be customized by your code) and <br/>
        /// <see cref="CancellationToken"/> (which indicates that the request was aborted from outside)
        /// </summary>
        public Func<List<LimitCheckResult>, IHttpRequestProxy, IHttpResponseProxy, CancellationToken, Task> ResponseFabric { get; set; }
    }
}
