using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
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
        public override int RetryAfterInSeconds => this.TimeoutInSeconds;

        /// <summary>
        /// When set to something > 0, the semaphore will be released not immediately 
        /// upon request completion, but after this number of seconds.
        /// This allows to implement request deduplication.
        /// </summary>
        public double ReleaseAfterSeconds { get; set; }

        /// <summary>
        /// ctor
        /// </summary>
        public SemaphoreRateLimitMethod()
        {
            this.ShouldThrowOnFailures = true;
        }

        /// <inheritdoc />
        public override async Task<int> IsExceededAsync(string limitKey, long cost, ICounterStore store, IHttpRequestProxy request)
        {
            var now = DateTimeOffset.UtcNow;

            var ttl = now + TimeSpan.FromSeconds(this.TimeoutInSeconds);

            long count = await store.IncrementAndGetAsync(
                limitKey,
                cost,
                ttl.UtcTicks,
                CounterStoreIncrementAndGetOptions.SetAbsoluteTtl,
                this.PermitLimit,
                request);

            if (count > this.PermitLimit)
            {
                await store.DecrementAsync(limitKey, cost, request);

                return -1;
            }
            else
            {
                return this.PermitLimit - (int)count;
            }
        }

        /// <inheritdoc />
        public override async Task<bool> IsStillExceededAsync(string limitKey, ICounterStore store, IHttpRequestProxy request)
        {
            long count = await store.GetAsync(limitKey, request);

            return count >= this.PermitLimit;
        }

        /// <inheritdoc />
        public override async Task DecrementAsync(string limitKey, long cost, ICounterStore store, IHttpRequestProxy request)
        {
            if (this.ReleaseAfterSeconds > 0)
            {
                // Delaying the semaphore release and doing it asynchronously

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

                Task.Delay(TimeSpan.FromSeconds(this.ReleaseAfterSeconds))
                    .ContinueWith(async _ =>
                    {
                        try
                        {
                            await store.DecrementAsync(limitKey, cost, request);
                        }
                        catch(Exception ex)
                        {
                            store.Log(LogLevel.Error, $"ThrottlingTroll failed. {ex}");
                        }
                    });

#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            }
            else
            {
                await store.DecrementAsync(limitKey, cost, request);
            }
        }

        /// <inheritdoc/>
        public override string GetCacheKey()
        {
            return $"{nameof(SemaphoreRateLimitMethod)}({this.PermitLimit},{this.TimeoutInSeconds})";
        }

        #region Telemetry
        internal override void AddTagsToActivity(Activity activity)
        {
            activity?.AddTag($"{nameof(SemaphoreRateLimitMethod)}.{nameof(this.PermitLimit)}", this.PermitLimit);
            activity?.AddTag($"{nameof(SemaphoreRateLimitMethod)}.{nameof(this.ShouldThrowOnFailures)}", base.ShouldThrowOnFailures);
            activity?.AddTag($"{nameof(SemaphoreRateLimitMethod)}.{nameof(this.IgnoreAllowList)}", this.IgnoreAllowList);
            activity?.AddTag($"{nameof(SemaphoreRateLimitMethod)}.{nameof(this.RetryAfterInSeconds)}", this.RetryAfterInSeconds);

            activity?.AddTag($"{nameof(SemaphoreRateLimitMethod)}.{nameof(this.TimeoutInSeconds)}", this.TimeoutInSeconds);
            activity?.AddTag($"{nameof(SemaphoreRateLimitMethod)}.{nameof(this.ReleaseAfterSeconds)}", this.ReleaseAfterSeconds);
        }
        #endregion
    }
}