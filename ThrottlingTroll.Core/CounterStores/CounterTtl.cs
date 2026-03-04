using System;

namespace ThrottlingTroll
{
    /// <summary>
    /// Either <see cref="CounterAbsoluteTtl"/> or <see cref="CounterIncrementalTtl"/> to be applied to the counter.
    /// </summary>
    /// <param name="MaxCounterValueToSetTtl">TTL will only be applied, if the new counter value is less or equal than this value.</param>
    public abstract record CounterTtl(long MaxCounterValueToSetTtl);

    /// <summary>
    /// Absolute TTL to be set for the counter.
    /// </summary>
    /// <param name="Ttl">Absolute TTL to be set.</param>
    /// <param name="MaxCounterValueToSetTtl">TTL will only be applied, if the new counter value is less or equal than this value.</param>
    public record CounterAbsoluteTtl(DateTimeOffset Ttl, long MaxCounterValueToSetTtl) : CounterTtl(MaxCounterValueToSetTtl);

    /// <summary>
    /// TTL increment to be applied to the counter. A newly created counter will get (DateTimeOffset.UtcNow + Ttl/>).
    /// </summary>
    /// <param name="Ttl">TTL increment.</param>
    /// <param name="MaxCounterValueToSetTtl">TTL will only be applied, if the new counter value is less or equal than this value.</param>
    public record CounterIncrementalTtl(TimeSpan Ttl, long MaxCounterValueToSetTtl): CounterTtl(MaxCounterValueToSetTtl);
}
