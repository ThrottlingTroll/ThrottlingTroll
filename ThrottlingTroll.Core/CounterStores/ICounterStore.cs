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
        /// Also sets TTL for it.
        /// </summary>
        Task<long> IncrementAndGetAsync(string key, DateTimeOffset ttl, bool isTtlSliding = false);

        /// <summary>
        /// Decrements counter by its key.
        /// </summary>
        Task DecrementAsync(string key);
    }
}
