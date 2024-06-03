
namespace ThrottlingTroll
{
    /// <summary>
    /// Supported rate limiting algorithms
    /// </summary>
    public enum RateLimitAlgorithm
    {
        /// <summary>
        /// Use <see cref="FixedWindowRateLimitMethod"/>
        /// </summary>
        FixedWindow = 0,

        /// <summary>
        /// Use <see cref="SlidingWindowRateLimitMethod"/>
        /// </summary>
        SlidingWindow,

        /// <summary>
        /// Use <see cref="SemaphoreRateLimitMethod"/>
        /// </summary>
        Semaphore
    }

    /// <summary>
    /// What to do on ThrottlingTroll's internal errors
    /// </summary>
    public enum ErrorHandlingBehavior
    {
        /// <summary>
        /// Use the rate limiting algorithm's default setting
        /// </summary>
        Unspecified = 0,

        /// <summary>
        /// Log the exception and allow the request to proceed
        /// </summary>
        LogAndContinue,

        /// <summary>
        /// Re-throw the original exception (and therefore fail the request)
        /// </summary>
        ThrowExceptions
    }

    /// <summary>
    /// Universal config setting for all rate limiting methods.
    /// </summary>
    public interface IRateLimitMethodSettings
    {
        /// <summary>
        /// Rate limiting algorithm to be used
        /// </summary>
        public RateLimitAlgorithm Algorithm { get; set; }

        /// <summary>
        /// Number of requests allowed per given period of time
        /// </summary>
        public int PermitLimit { get; set; }

        /// <summary>
        /// Window size in seconds
        /// </summary>
        public int IntervalInSeconds { get; set; }

        /// <summary>
        /// (Specific to <see cref="SlidingWindowRateLimitMethod"/>) Number of intervals to divide the window into. Should not be less than <see cref="IntervalInSeconds"/>.
        /// </summary>
        public int NumOfBuckets { get; set; }

        /// <summary>
        /// (Specific to <see cref="SemaphoreRateLimitMethod"/>) Timeout in seconds. Defaults to 100.
        /// </summary>
        public int TimeoutInSeconds { get; set; }

        /// <summary>
        /// (Specific to <see cref="SemaphoreRateLimitMethod"/>)
        /// When set to something > 0, the semaphore will be released not immediately 
        /// upon request completion, but after this number of seconds.
        /// This allows to implement request deduplication.
        /// </summary>
        public int ReleaseAfterSeconds { get; set; }

        /// <summary>
        /// What to do on ThrottlingTroll's internal errors
        /// </summary>
        public ErrorHandlingBehavior ErrorHandlingBehavior { get; set; }
    }
}