using System.Runtime.CompilerServices;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("ThrottlingTroll.Core.Tests")]

namespace ThrottlingTroll
{
    /// <summary>
    /// Base class for all limit methods
    /// </summary>
    public abstract class RateLimitMethod
    {
        /// <summary>
        /// Number of requests allowed per given period of time
        /// </summary>
        public int PermitLimit { get; set; }

        /// <summary>
        /// Increments the counter by cost and checks if limit of calls is exceeded for a given rule (identified by its hash).
        /// If exceeded, returns number of seconds to retry after. Otherwise returns 0.
        /// </summary>
        public abstract Task<int> IsExceededAsync(string limitKey, long cost, ICounterStore store);

        /// <summary>
        /// Checks if limit of calls is exceeded for a given rule (identified by its hash).
        /// Used for implementing delayed responses.
        /// </summary>
        public abstract Task<bool> IsStillExceededAsync(string limitKey, ICounterStore store);

        /// <summary>
        /// Decrements the given counter by cost _if the rate limit method supports this functionality_.
        /// Otherwise should do nothing.
        /// </summary>
        public abstract Task DecrementAsync(string limitKey, long cost, ICounterStore store);

        /// <summary>
        /// Generate a unique key per rule
        /// Otherwise should do nothing.
        /// </summary>
        public abstract string GetCacheKey();

        /// <summary>
        /// Whether ThrottlingTroll's internal failures should result in exceptions or in just log entries.
        /// </summary>
        public bool ShouldThrowOnFailures { get; set; } = false;
    }
}