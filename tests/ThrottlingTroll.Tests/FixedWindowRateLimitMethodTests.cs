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
            IntervalInSeconds = 1,
        };

        // If too close to the end of current second, need to wait
        if (DateTime.UtcNow.Millisecond > 800)
        {
            Thread.Sleep(200);
        }

        Trace.WriteLine($"{DateTime.Now.ToString("o")} Started");

        for (int i = 0; i < limiter.PermitLimit; i++) 
        {
            Assert.AreEqual(0, await limiter.IsExceededAsync(key, 1, store));
        }

        // Now we should exceed
        Assert.AreNotEqual(0, await limiter.IsExceededAsync(key, 1, store));
        Assert.AreNotEqual(0, await limiter.IsExceededAsync(key, 1, store));
        Assert.AreNotEqual(0, await limiter.IsExceededAsync(key, 1, store));

        // Now waiting for the next second to start
        Trace.WriteLine($"{DateTime.Now.ToString("o")} Waiting till next second");

        int ms = DateTime.UtcNow.Millisecond;
        do
        {
            Thread.Sleep(100);
        }
        while (DateTime.UtcNow.Millisecond > ms);

        Trace.WriteLine($"{DateTime.Now.ToString("o")} Reached next second");

        // Now we should be good again
        for (int i = 0; i < limiter.PermitLimit; i++)
        {
            Assert.AreEqual(0, await limiter.IsExceededAsync(key, 1, store));
        }

        // Now we should exceed again
        Assert.AreNotEqual(0, await limiter.IsExceededAsync(key, 1, store));

        Trace.WriteLine($"{DateTime.Now.ToString("o")} Finished");
    }
}