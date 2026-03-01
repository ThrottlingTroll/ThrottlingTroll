using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// Implements a Leaky Bucket algorithm
    /// </summary>
    public class LeakyBucketRateLimitMethod : RateLimitMethod
    {
        /// <summary>
        /// Window size in seconds
        /// </summary>
        public int IntervalInSeconds { get; set; }

        /// <inheritdoc />
        public override int RetryAfterInSeconds => this.IntervalInSeconds;

        /// <inheritdoc />
        public override async Task<int> IsExceededAsync(string limitKey, long cost, ICounterStore store, IHttpRequestProxy request)
        {
            if (this.IntervalInSeconds <= 0 || this.PermitLimit <= 0)
            {
                return int.MaxValue;
            }

            long leakageInTicks = this.IntervalInSeconds * 10000000 / this.PermitLimit;

            long count = await store.IncrementAndGetAsync(
                limitKey,
                cost,
                leakageInTicks,
                CounterStoreIncrementAndGetOptions.IncrementTtl,
                maxCounterValueToSetTtl: this.PermitLimit, // bumping up the TTL only so long as the queue is not overflown
                request);

            if (count > this.PermitLimit)
            {
                return -1;
            }
            else
            {
                // Adding a delay, according to this request's position in the queue
                long delayInTicks = leakageInTicks * (count - 1);
                await Task.Delay(TimeSpan.FromTicks(delayInTicks));

                return this.PermitLimit - (int)count;
            }
        }

        /// <inheritdoc />
        public override async Task<bool> IsStillExceededAsync(string limitKey, ICounterStore store, IHttpRequestProxy request)
        {
            if (this.IntervalInSeconds <= 0)
            {
                return false;
            }

            long count = await store.GetAsync(limitKey, request);

            return count >= this.PermitLimit;
        }

        /// <inheritdoc />
        public override Task DecrementAsync(string limitKey, long cost, ICounterStore store, IHttpRequestProxy request)
        {
            // Doing nothing
            return Task.CompletedTask;
        }
        
        /// <inheritdoc/>
        public override string GetCacheKey()
        {
            return $"{nameof(LeakyBucketRateLimitMethod)}({this.PermitLimit},{this.IntervalInSeconds})";
        }

        #region Telemetry
        internal override void AddTagsToActivity(Activity activity)
        {
            activity?.AddTag($"{nameof(LeakyBucketRateLimitMethod)}.{nameof(this.PermitLimit)}", this.PermitLimit);
            activity?.AddTag($"{nameof(LeakyBucketRateLimitMethod)}.{nameof(this.ShouldThrowOnFailures)}", base.ShouldThrowOnFailures);
            activity?.AddTag($"{nameof(LeakyBucketRateLimitMethod)}.{nameof(this.IgnoreAllowList)}", this.IgnoreAllowList);
            activity?.AddTag($"{nameof(LeakyBucketRateLimitMethod)}.{nameof(this.RetryAfterInSeconds)}", this.RetryAfterInSeconds);

            activity?.AddTag($"{nameof(LeakyBucketRateLimitMethod)}.{nameof(this.IntervalInSeconds)}", this.IntervalInSeconds);
        }
        #endregion
    }
}