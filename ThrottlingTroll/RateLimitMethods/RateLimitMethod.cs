using System.Runtime.CompilerServices;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("ThrottlingTroll.Tests")]

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
        internal abstract Task<int> IsExceededAsync(string limitKey, ICounterStore store);
    }
}