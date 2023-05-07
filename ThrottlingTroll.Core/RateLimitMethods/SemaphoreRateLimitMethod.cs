using System;
using System.Runtime.Caching;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// Implements a Semaphore (Concurrency Limiter) throttling algorithm
    /// </summary>
    public class SemaphoreRateLimitMethod : RateLimitMethod
    {
        /// <summary>
        /// Window size in seconds
        /// </summary>
        public int IntervalInSeconds { get; set; }

        /// <inheritdoc />
        public override async Task<int> IsExceededAsync(string limitKey, ICounterStore store)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public override async Task DecrementAsync(string limitKey, ICounterStore store)
        {
            throw new NotImplementedException();
        }
    }
}