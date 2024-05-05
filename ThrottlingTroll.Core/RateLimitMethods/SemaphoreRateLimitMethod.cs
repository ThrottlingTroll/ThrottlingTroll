﻿using System;
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
        /// ctor
        /// </summary>
        public SemaphoreRateLimitMethod()
        {
            this.ShouldThrowOnFailures = true;
        }

        /// <inheritdoc />
        public override async Task<int> IsExceededAsync(string limitKey, long cost, ICounterStore store, IHttpRequestProxy request)
        {
            var now = DateTime.UtcNow;

            var ttl = now + TimeSpan.FromSeconds(this.TimeoutInSeconds);

            long count = await store.IncrementAndGetAsync(limitKey, cost, ttl, this.PermitLimit, request);

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
        public override Task DecrementAsync(string limitKey, long cost, ICounterStore store, IHttpRequestProxy request)
        {
            return store.DecrementAsync(limitKey, cost, request);
        }

        /// <inheritdoc/>
        public override string GetCacheKey()
        {
            return $"{nameof(SemaphoreRateLimitMethod)}({this.PermitLimit},{this.TimeoutInSeconds})";
        }
    }
}