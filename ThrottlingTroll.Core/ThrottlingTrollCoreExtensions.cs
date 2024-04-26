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
        public static Func<Task<ThrottlingTrollConfig>> MergeAllConfigSources(ThrottlingTrollConfig config, Func<Task<ThrottlingTrollConfig>> initialConfigFunc, IServiceProvider serviceProvider)
        {
            config ??= new ThrottlingTrollConfig();

            config.MergeWith(ThrottlingTrollConfig.FromConfigSection(serviceProvider));

            if (initialConfigFunc == null)
            {
                return () => Task.FromResult(config);
            }
            else
            {
                return () => initialConfigFunc().ContinueWith(t => t.Result.MergeWith(config));
            }
        }
    }
}
