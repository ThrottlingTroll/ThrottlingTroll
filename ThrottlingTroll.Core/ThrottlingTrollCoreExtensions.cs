using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// General-purpose extension methods
    /// </summary>
    public static class ThrottlingTrollCoreExtensions
    {
        /// <summary>
        /// Concatenates two nullable lists
        /// </summary>
        public static IList<T> UnionOf<T>(IList<T> first, IList<T> second) 
        {
            if (first == null)
            {
                return second;
            }

            if (second == null) 
            {
                return first;
            }

            return first.Concat(second).ToList();
        } 

        /// <summary>
        /// Adds or appends a list of items to a dictionary key
        /// </summary>
        public static void AddItemsToKey<T>(this IDictionary<object, object> map, string key, IEnumerable<T> items)
        {
            // Should always put a _new_ List<T> object here. Otherwise multiple middleware instances might start interfering with each other.
            map.TryAdd(key, new List<T>());

            ((List<T>)map[key]).AddRange(items);
        }

        /// <summary>
        /// Makes sure an asynchronous lambda is executed just once
        /// </summary>
        public static Func<Task> RunOnce(this Func<Task> func)
        {
            bool wasCalled = false;

            return async () =>
            {
                if (!wasCalled)
                {
                    wasCalled = true;

                    await func();
                }
            };
        }

        /// <summary>
        /// Collects and merges all config sources. Returns them in form of a new GetConfigFunc.
        /// </summary>
        public static Func<Task<ThrottlingTrollConfig>> MergeAllConfigSources(
            ThrottlingTrollConfig config,
            ThrottlingTrollConfig declarativeConfig,
            Func<Task<ThrottlingTrollConfig>> initialConfigFunc, 
            IServiceProvider serviceProvider
        )
        {
            config ??= new ThrottlingTrollConfig();

            config.MergeWith(ThrottlingTrollConfig.FromConfigSection(serviceProvider));

            config.MergeWith(declarativeConfig);

            if (initialConfigFunc == null)
            {
                return () => Task.FromResult(config);
            }
            else
            {
                return () => initialConfigFunc().ContinueWith(t => t.Result.MergeWith(config));
            }
        }

        /// <summary>
        /// Converts <see cref="IRateLimitMethodSettings"/> to <see cref="RateLimitMethod"/>
        /// </summary>
        public static RateLimitMethod ToRateLimitMethod(this IRateLimitMethodSettings settings)
        {
            switch (settings.Algorithm)
            {
                case RateLimitAlgorithm.FixedWindow:
                    return new FixedWindowRateLimitMethod
                    {
                        PermitLimit = settings.PermitLimit,
                        IntervalInSeconds = settings.IntervalInSeconds,
                        ShouldThrowOnFailures = settings.ShouldThrowOnFailures ?? false
                    };
                case RateLimitAlgorithm.SlidingWindow:
                    return new SlidingWindowRateLimitMethod
                    {
                        PermitLimit = settings.PermitLimit,
                        IntervalInSeconds = settings.IntervalInSeconds,
                        NumOfBuckets = settings.NumOfBuckets,
                        ShouldThrowOnFailures = settings.ShouldThrowOnFailures ?? false
                    };
                case RateLimitAlgorithm.Semaphore:
                    return new SemaphoreRateLimitMethod
                    {
                        PermitLimit = settings.PermitLimit,
                        TimeoutInSeconds = settings.TimeoutInSeconds,
                        // Intentionally setting this to true by default for SemaphoreRateLimitMethod
                        ShouldThrowOnFailures = settings.ShouldThrowOnFailures ?? true
                    };
            }

            throw new InvalidOperationException("Failed to initialize ThrottlingTroll. Rate limit algorithm not recognized.");
        }

        /// <summary>
        /// Trims a string from the end of another string, if that another string ends with it.
        /// </summary>
        public static string TrimSuffix(this string str, string suffix)
        {
            if (str.EndsWith(suffix, StringComparison.InvariantCultureIgnoreCase))
            {
                return str.Substring(0, str.Length - suffix.Length);
            }

            return str;
        }
    }
}
