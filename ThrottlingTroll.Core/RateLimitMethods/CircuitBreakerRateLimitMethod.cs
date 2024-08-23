﻿using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// Implements Circuit Breaker algorithm
    /// </summary>
    public class CircuitBreakerRateLimitMethod : RateLimitMethod
    {
        /// <summary>
        /// Window size in seconds
        /// </summary>
        public int IntervalInSeconds { get; set; }

        /// <summary>
        /// Defines how often to check whether the resource has healed itself.
        /// Once a failure limit is exceeded, the request rate will be limited to 1 request per this timeframe.
        /// </summary>
        public int TrialIntervalInSeconds { get; set; } = 100;

        /// <inheritdoc />
        public override int RetryAfterInSeconds => this.IntervalInSeconds;

        /// <inheritdoc />
        public override async Task<int> IsExceededAsync(string limitKey, long cost, ICounterStore _, IHttpRequestProxy request)
        {
            // This method applies limits only when in Trial state
            if (this.TrialIntervalInSeconds <= 0 || !IsUnderTrial(limitKey))
            {
                return int.MaxValue;
            }

            var now = DateTime.UtcNow;

            var ttl = now - TimeSpan.FromMilliseconds(now.Millisecond) + TimeSpan.FromSeconds(this.TrialIntervalInSeconds);

            // Checking the failure count in the last ProbationIntervalInSeconds
            long count = await Store.IncrementAndGetAsync(limitKey, cost, ttl, 1, request);

            if (count > this.PermitLimit)
            {
                return -1;
            }
            else
            {
                return this.PermitLimit - (int)count;
            }
        }

        /// <inheritdoc />
        public override async Task<bool> IsStillExceededAsync(string limitKey, ICounterStore _, IHttpRequestProxy request)
        {
            // This method applies limits only when in Trial state
            if (this.TrialIntervalInSeconds <= 0 || !IsUnderTrial(limitKey))
            {
                return false;
            }

            long count = await Store.GetAsync(limitKey, request);

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
            return $"{nameof(FixedWindowRateLimitMethod)}({this.PermitLimit},{this.IntervalInSeconds})";
        }

        /// <summary>
        /// Checks whether this particular response or this particular exception is considered a failure 
        /// by this particular limit instance
        /// </summary>
        protected internal virtual bool IsFailed(IHttpResponseProxy response, Exception exception)
        {
            if (this.IntervalInSeconds <= 0 || this.TrialIntervalInSeconds <= 0)
            {
                return false;
            }

            if (exception != null)
            {
                return true;
            }

            return response?.StatusCode < 200 || response?.StatusCode > 299;
        }

        /// <summary>
        /// Puts a given CircuitBreaker limit into trial mode
        /// </summary>
        internal static void PutIntoTrial(string limitKey)
        {
            TrialMap[limitKey] = true;
        }

        /// <summary>
        /// Lifts trial mode for a given CircuitBreaker limit
        /// </summary>
        internal static void ReleaseFromTrial(string limitKey)
        {
            TrialMap.TryRemove(limitKey, out var _);
        }

        /// <summary>
        /// Checks if a given CircuitBreaker limit is in trial mode
        /// </summary>
        internal static bool IsUnderTrial(string limitKey)
        {
            return TrialMap.ContainsKey(limitKey);
        }

        private static readonly ConcurrentDictionary<string, bool> TrialMap = new ConcurrentDictionary<string, bool>();

        /// <summary>
        /// This method uses its own private MemoryCacheCounterStore, because the limit should be applied on a per-instance basis
        /// </summary>
        private static readonly ICounterStore Store = new MemoryCacheCounterStore();
    }
}