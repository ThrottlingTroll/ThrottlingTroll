using System.Collections.Generic;
using System.Linq;

namespace ThrottlingTroll
{
    /// <summary>
    /// Context passed to the onLimitExceeded handler.
    /// </summary>
    public class ThrottlingTrollContext
    {
        internal ThrottlingTrollContext() { }

        /// <summary>
        /// Results of checking all applicable rules. Rules that were exceeded have <see cref="LimitCheckResult.RequestsRemaining"/> below zero.
        /// </summary>
        public IList<LimitCheckResult> LimitCheckResults { get; internal set; }

        /// <summary>
        /// Information about the limit that was exceeded.
        /// </summary>
        public LimitCheckResult ExceededLimit
        {
            get => this.LimitCheckResults
                .Where(r => r.RequestsRemaining < 0)
                // Sorting by the suggested RetryAfter header value (which is expected to be in seconds) in descending order
                .OrderByDescending(r => r.RetryAfterInSeconds)
                .FirstOrDefault();
        }

        /// <summary>
        /// Set this to true, if you want ThrottlingTroll to continue processing the call as normal
        /// even when a limit was exceeded.
        /// </summary>
        public bool ShouldContinueAsNormal { get; set; }
    }
}
