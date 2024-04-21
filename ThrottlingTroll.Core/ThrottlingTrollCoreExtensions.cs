using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// General-purpose extension methods
    /// </summary>
    public static class ThrottlingTrollCoreExtensions
    {
        /// <summary>
        /// Adds or appends a list of items to a dictionary key
        /// </summary>
        public static void AddItemsToKey<T>(this IDictionary<object, object> map, string key, List<T> items)
        {
            if (map.TryGetValue(key, out var existingObject))
            {
                ((List<T>)existingObject).AddRange(items);
            }
            else
            {
                map[key] = items;
            }
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
    }
}
