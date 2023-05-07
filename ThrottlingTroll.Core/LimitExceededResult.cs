
namespace ThrottlingTroll
{
    /// <summary>
    /// Result of checking a limit
    /// </summary>
    public class LimitExceededResult
    {
        /// <summary>
        /// Suggested value for Retry-After response header
        /// </summary>
        public string RetryAfterHeaderValue { get; private set; }

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
        public ThrottlingTrollRule RuleThatWasExceeded { get; private set; }

        internal LimitExceededResult(bool isExceeded, ThrottlingTrollRule rule, string retryAfter, string counterId)
        {
            this.IsExceeded = isExceeded;
            this.RuleThatWasExceeded = rule;
            this.RetryAfterHeaderValue = retryAfter;
            this.CounterId = counterId;
        }

        /// <summary>
        /// Ctor to be used when propagating from egress to ingress
        /// </summary>
        public LimitExceededResult(string retryAfter)
        {
            this.RetryAfterHeaderValue = retryAfter;
        }

        internal bool IsExceeded { get; private set; }
    }
}
