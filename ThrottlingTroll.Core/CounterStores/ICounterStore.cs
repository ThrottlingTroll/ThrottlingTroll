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
        /// <param name="request">Incoming request, for reference</param>
        Task<long> GetAsync(string key, IHttpRequestProxy request);

        /// <summary>
        /// Increments and gets counter by its key.
        /// Also sets/increments TTL for it, but only if the resulting counter is less or equal than maxCounterValueToSetTtl.
        /// </summary>
        /// <param name="key">Counter's key</param>
        /// <param name="cost">Value to increment by</param>
        /// <param name="ttlInTicks">TTL for this counter</param>
        /// <param name="options">Defines how ttlInTicks are handled</param>
        /// <param name="maxCounterValueToSetTtl">TTL will only be set, if the counter value is less or equal to this number</param>
        /// <param name="request">Incoming request, for reference</param>
        /// <returns>New counter value</returns>
        Task<long> IncrementAndGetAsync(
            string key,
            long cost,
            long ttlInTicks,
            CounterStoreIncrementAndGetOptions options,
            long maxCounterValueToSetTtl,
            IHttpRequestProxy request);

        /// <summary>
        /// Decrements counter by its key.
        /// </summary>
        /// <param name="key">Counter's key</param>
        /// <param name="cost">Value to decrement by</param>
        /// <param name="request">Incoming request, for reference</param>
        Task DecrementAsync(string key, long cost, IHttpRequestProxy request);

        /// <summary>
        /// Logging utility to use. Will be set by ThrottlingTroll, so don't override it yourself.
        /// </summary>
        public Action<LogLevel, string> Log { get; set; }
    }
}
