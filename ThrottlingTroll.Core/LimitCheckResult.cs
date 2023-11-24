
namespace ThrottlingTroll
{
    /// <summary>
    /// Result of checking a limit
    /// </summary>
    public class LimitCheckResult
    {
        /// <summary>
        /// The remaining amount of requests allowed within current timeframe.
        /// Goes below zero, when a limit is exceeded.
        /// </summary>
        public int RequestsRemaining { get; set; }

        /// <summary>
        /// Suggested value for Retry-After response header
        /// </summary>
        public string RetryAfterHeaderValue { get; private set; }

        /// <summary>
        /// Suggested number of seconds to retry after
        /// </summary>
        public int RetryAfterInSeconds { get; private set; }

        /// <summary>
        /// Unique ID of the counter that was exceeded. Basically it is a hash of the relevant Rate Limiting Rule
        /// plus optional Identity ID (if <see cref="RequestFilter.IdentityIdExtractor"/> is specified).
        /// Will be null for egress-to-ingress-propagated results.
        /// </summary>
        public string CounterId { get; private set; }

        /// <summary>
        /// Reference to the Rate Limiting Rule, that was exceeded.
        /// Will be null for egress-to-ingress-propagated results.
        /// </summary>
        public ThrottlingTrollRule Rule { get; private set; }

        internal LimitCheckResult(int requestsRemaining, ThrottlingTrollRule rule, int retryAfterInSeconds, string counterId)
        {
            this.RequestsRemaining = requestsRemaining;
            this.Rule = rule;
            this.RetryAfterInSeconds = retryAfterInSeconds;
            this.RetryAfterHeaderValue = retryAfterInSeconds.ToString();
            this.CounterId = counterId;
        }

        /// <summary>
        /// Ctor to be used when propagating from egress to ingress
        /// </summary>
        public LimitCheckResult(string retryAfter)
        {
            this.RetryAfterHeaderValue = retryAfter;
            this.RequestsRemaining = -1;
        }
    }
}
