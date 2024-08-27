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
        public int TrialIntervalInSeconds { get; set; }

        /// <inheritdoc />
        public int NumOfBuckets { get; set; }

        /// <inheritdoc />
        public int TimeoutInSeconds { get; set; } = 100;

        /// <inheritdoc />
        public int ReleaseAfterSeconds { get; set; }

        /// <summary>
        /// Whether ThrottlingTroll's internal failures should result in exceptions or in just log entries.
        /// </summary>
        [Obsolete("Use ErrorHandlingBehavior instead")]
        public bool? ShouldThrowOnFailures 
        { 
            get 
            {
                return this.ErrorHandlingBehavior == ErrorHandlingBehavior.Unspecified ? 
                    null : 
                    (this.ErrorHandlingBehavior == ErrorHandlingBehavior.ThrowExceptions);
            }
            
            set 
            {
                this.ErrorHandlingBehavior = value.HasValue ?
                    (value.Value ? ErrorHandlingBehavior.ThrowExceptions : ErrorHandlingBehavior.LogAndContinue) : 
                    ErrorHandlingBehavior.Unspecified;
            } 
        }

        /// <inheritdoc />
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ErrorHandlingBehavior ErrorHandlingBehavior { get; set; }
    }
}