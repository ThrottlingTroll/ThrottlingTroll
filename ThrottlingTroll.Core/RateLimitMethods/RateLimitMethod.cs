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
        /// Checks if limit of calls is exceeded for a given rule (identified by its hash).
        /// If exceeded, returns number of seconds to retry after. Otherwise returns 0.
        /// </summary>
        public abstract Task<int> IsExceededAsync(string limitKey, ICounterStore store);

        /// <summary>
        /// Decrements the given counter _if the rate limit method supports this functionality_.
        /// Otherwise should do nothing.
        /// </summary>
        public abstract Task DecrementAsync(string limitKey, ICounterStore store);
    }
}