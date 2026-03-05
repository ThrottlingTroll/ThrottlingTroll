using System.Diagnostics;

namespace ThrottlingTroll.Tests;

[TestClass]
public class FixedWindowRateLimitMethodTests
{
    [TestMethod]
    public async Task IsExceededAsyncDoesTheJob()
    {
        string key = Guid.NewGuid().ToString();

        var store = new MemoryCacheCounterStore();

        var limiter = new FixedWindowRateLimitMethod
        {
            PermitLimit = 3,
            IntervalInSeconds = 0.1,
        };

        Trace.WriteLine($"{DateTime.Now:o} Started");

        for (int i = 0; i < limiter.PermitLimit; i++) 
        {
            Assert.AreNotEqual(-1, await limiter.IsExceededAsync(key, 1, store, null));
        }

        // Now we should exceed
        Assert.AreEqual(-1, await limiter.IsExceededAsync(key, 1, store, null));
        Assert.AreEqual(-1, await limiter.IsExceededAsync(key, 1, store, null));
        await Task.Delay(Random.Shared.Next(0, 30));
        Assert.AreEqual(-1, await limiter.IsExceededAsync(key, 1, store, null));

        await Task.Delay(110);

        // Now we should be good again
        for (int i = 0; i < limiter.PermitLimit; i++)
        {
            Assert.AreNotEqual(-1, await limiter.IsExceededAsync(key, 1, store, null));
        }

        // Now we should exceed again
        Assert.AreEqual(-1, await limiter.IsExceededAsync(key, 1, store, null));

        Trace.WriteLine($"{DateTime.Now.ToString("o")} Finished");
    }
}