using System;
using System.Text.Json.Serialization;

namespace ThrottlingTroll
{
    /// <summary>
    /// Universal config setting for all rate limiting methods.
    /// Used for polymorphic deserialization.
    /// </summary>
    public class RateLimitMethodSettings : IRateLimitMethodSettings
    {
        /// <inheritdoc />
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public RateLimitAlgorithm Algorithm { get; set; }

        /// <inheritdoc />
        public int PermitLimit { get; set; }

        /// <inheritdoc />
        public int IntervalInSeconds { get; set; }

        /// <inheritdoc />
        public int NumOfBuckets { get; set; }

        /// <inheritdoc />
        public int TimeoutInSeconds { get; set; } = 100;

        /// <inheritdoc />
        public int ReleaseAfterSeconds { get; set; }

        /// <inheritdoc />
        public bool? ShouldThrowOnFailures { get; set; }
    }
}