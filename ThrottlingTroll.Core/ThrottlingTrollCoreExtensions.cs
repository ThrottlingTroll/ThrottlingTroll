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
        /// Makes sure a lambda is executed just once
        /// </summary>
        public static Action<T> RunOnce<T>(this Action<T> func)
        {
            bool wasCalled = false;

            return (T param) =>
            {
                if (!wasCalled)
                {
                    wasCalled = true;

                    func(param);
                }
            };
        }

        /// <summary>
        /// Makes sure an asynchronous lambda is executed just once
        /// </summary>
        public static Func<T, Task> RunOnce<T>(this Func<T, Task> func)
        {
            bool wasCalled = false;

            return async (T param) =>
            {
                if (!wasCalled)
                {
                    wasCalled = true;

                    await func(param);
                }
            };
        }
    }
}
