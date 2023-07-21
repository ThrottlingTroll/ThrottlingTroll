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
        Task<long> GetAsync(string key);

        /// <summary>
        /// Increments and gets counter by its key.
        /// Also sets TTL for it, but only if the resulting counter is less or equal than maxCounterValueToSetTtl.
        /// </summary>
        Task<long> IncrementAndGetAsync(string key, DateTimeOffset ttl, long maxCounterValueToSetTtl = 1);

        /// <summary>
        /// Decrements counter by its key.
        /// </summary>
        Task DecrementAsync(string key);

        /// <summary>
        /// Logging utility to use. Will be set by ThrottlingTroll, so don't override it yourself.
        /// </summary>
        public Action<LogLevel, string> Log { set; }
    }
}
