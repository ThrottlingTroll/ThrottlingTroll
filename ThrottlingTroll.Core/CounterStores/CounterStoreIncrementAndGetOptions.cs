
namespace ThrottlingTroll
{
    /// <summary>
    /// Defines how 
    /// <see cref="ICounterStore.IncrementAndGetAsync"/>
    /// should handle its ttlInTicks parameter.
    /// </summary>
    public enum CounterStoreIncrementAndGetOptions
    {
        /// <summary>
        /// ttlInTicks is treated as an absolute TTL (UTC ticks)
        /// </summary>
        SetAbsoluteTtl = 0,

        /// <summary>
        /// TTL is incremented by ttlInTicks
        /// </summary>
        IncrementTtl = 1,
    }
}
