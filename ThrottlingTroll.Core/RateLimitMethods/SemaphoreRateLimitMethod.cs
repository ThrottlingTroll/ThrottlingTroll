using System;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// Implements a Semaphore (Concurrency Limiter) throttling algorithm
    /// </summary>
    public class SemaphoreRateLimitMethod : RateLimitMethod
    {
        /// <summary>
        /// Timeout in seconds. Defaults to 100.
        /// </summary>
        public int TimeoutInSeconds { get; set; } = 100;

        /// <inheritdoc />
        public override async Task<int> IsExceededAsync(string limitKey, ICounterStore store)
        {
            var now = DateTime.UtcNow;

            var ttl = now + TimeSpan.FromSeconds(this.TimeoutInSeconds);

            long count = await store.IncrementAndGetAsync(limitKey, ttl, this.PermitLimit);

            if (count > this.PermitLimit)
            {
                await store.DecrementAsync(limitKey);

                return this.TimeoutInSeconds;
            }
            else
            {
                return 0;
            }
        }

        /// <inheritdoc />
        public override async Task<bool> IsStillExceededAsync(string limitKey, ICounterStore store)
        {
            long count = await store.GetAsync(limitKey);

            return count >= this.PermitLimit;
        }

        /// <inheritdoc />
        public override Task DecrementAsync(string limitKey, ICounterStore store)
        {
            return store.DecrementAsync(limitKey);
        }
    }
}