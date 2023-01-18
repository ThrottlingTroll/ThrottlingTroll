using System.Runtime.Caching;

namespace ThrottlingTroll.Tests;

internal static class Helpers
{
    public static void Flush(this MemoryCache cache)
    {
        var keys = cache.Select(i => i.Key).ToArray();
        foreach (var k in keys)
        {
            MemoryCache.Default.Remove(k);
        }
    }
}
