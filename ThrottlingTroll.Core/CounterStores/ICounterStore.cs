using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// Represents a Store for request counters
    /// </summary>
    public interface ICounterStore
    {
        /// <summary>
        /// Gets counter by its key
        /// </summary>
        /// <param name="key">Counter's key</param>
        Task<long> GetAsync(string key);

        /// <summary>
        /// Increments and gets counter by its key.
        /// Also sets TTL for it, but only if the resulting counter is less or equal than maxCounterValueToSetTtl.
        /// </summary>
        /// <param name="key">Counter's key</param>
        /// <param name="cost">Value to increment by</param>
        /// <param name="ttl">TTL for this counter</param>
        /// <param name="maxCounterValueToSetTtl">TTL will only be set, if the counter value is less or equal to this number</param>
        /// <returns>New counter value</returns>
        Task<long> IncrementAndGetAsync(string key, long cost, DateTimeOffset ttl, long maxCounterValueToSetTtl = 1);

        /// <summary>
        /// Decrements counter by its key.
        /// </summary>
        /// <param name="key">Counter's key</param>
        /// <param name="cost">Value to decrement by</param>
        Task DecrementAsync(string key, long cost);

        /// <summary>
        /// Logging utility to use. Will be set by ThrottlingTroll, so don't override it yourself.
        /// </summary>
        public Action<LogLevel, string> Log { set; }
    }
}
