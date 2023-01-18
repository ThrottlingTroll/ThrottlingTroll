using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// Implements a Fixed Window throttling algorithm
    /// </summary>
    public class FixedWindowRateLimitMethod : RateLimitMethod
    {
        /// <summary>
        /// Window size in seconds
        /// </summary>
        public int IntervalInSeconds { get; set; }

        /// <inheritdoc />
        internal override async Task<int> IsExceededAsync(string limitKey, ICounterStore store)
        {
            if (this.IntervalInSeconds <= 0)
            {
                return 0;
            }

            var ttl = DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(DateTimeOffset.UtcNow.Millisecond) + TimeSpan.FromSeconds(this.IntervalInSeconds);

            long count = await store.IncrementAndGetAsync(limitKey, ttl);

            if (count > this.PermitLimit)
            {
                return this.IntervalInSeconds;
            }
            else
            {
                return 0;
            }
        }
    }
}