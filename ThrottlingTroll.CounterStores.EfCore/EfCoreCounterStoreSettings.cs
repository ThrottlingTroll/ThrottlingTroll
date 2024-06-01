
namespace ThrottlingTroll.CounterStores.EfCore
{
    /// <summary>
    /// Custom settings for <see cref="EfCoreCounterStore"/>.
    /// </summary>
    public class EfCoreCounterStoreSettings
    {
        /// <summary>
        /// How often to execute the table cleanup routine.
        /// </summary>
        public double RunCleanupEveryMinutes { get; set; } = 1;

        /// <summary>
        /// How much expired a counter should be for the table cleanup routine to delete it.
        /// </summary>
        public double DeleteExpiredCountersAfterMinutes { get; set; } = 10;

        /// <summary>
        /// Every DB operation is done with retries. Here is the number of attempts to make before failing.
        /// </summary>
        public int MaxAttempts { get; set; } = 10;

        /// <summary>
        /// A random delay between 0 and this value will be added before every retry.
        /// </summary>
        public int MaxDelayBetweenAttemptsInMilliseconds { get; set; } = 100;
    }
}
