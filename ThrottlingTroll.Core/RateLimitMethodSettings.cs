﻿using System;
using System.Text.Json.Serialization;

namespace ThrottlingTroll
{
    /// <summary>
    /// Supported rate limiting algorithms
    /// </summary>
    public enum RateLimitAlgorithm
    {
        FixedWindow = 0,
        SlidingWindow,
        Semaphore
    }

    /// <summary>
    /// Universal config setting for all rate limiting methods.
    /// Used for polymorphic deserialization.
    /// </summary>
    public class RateLimitMethodSettings
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public RateLimitAlgorithm Algorithm { get; set; }

        public int PermitLimit { get; set; }
        public int IntervalInSeconds { get; set; }
        public int NumOfBuckets { get; set; }
        public int TimeoutInSeconds { get; set; } = 100;

        public RateLimitMethod ToRateLimitMethod()
        {
            switch (this.Algorithm)
            {
                case RateLimitAlgorithm.FixedWindow:
                    return new FixedWindowRateLimitMethod
                    {
                        PermitLimit = this.PermitLimit,
                        IntervalInSeconds = this.IntervalInSeconds
                    };
                case RateLimitAlgorithm.SlidingWindow:
                    return new SlidingWindowRateLimitMethod
                    {
                        PermitLimit = this.PermitLimit,
                        IntervalInSeconds = this.IntervalInSeconds,
                        NumOfBuckets = this.NumOfBuckets
                    };
                case RateLimitAlgorithm.Semaphore:
                    return new SemaphoreRateLimitMethod
                    {
                        PermitLimit = this.PermitLimit,
                        TimeoutInSeconds = this.TimeoutInSeconds
                    };
            }

            throw new InvalidOperationException("Failed to initialize ThrottlingTroll from settings. Rate limit algorithm not recognized.");
        }
    }
}