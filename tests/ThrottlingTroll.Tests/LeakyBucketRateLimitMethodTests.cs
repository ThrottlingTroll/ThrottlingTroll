using System.Diagnostics;

namespace ThrottlingTroll.Tests;

[TestClass]
public class LeakyBucketRateLimitMethodTests
{
    [TestMethod]
    public async Task OneRequestPerIntervalBehavesLikeFixedWindow()
    {
        string key = Guid.NewGuid().ToString();

        var store = new MemoryCacheCounterStore();

        var limiter = new LeakyBucketRateLimitMethod
        {
            PermitLimit = 1,
            IntervalInSeconds = 0.3,
        };

        for (int i = 0; i < 3; i++)
        {
            var sw = Stopwatch.StartNew();

            var tasks = Enumerable
                .Range(0, 3)
                .Select(i => limiter.IsExceededAsync(key, 1, store, null))
                .ToArray();
            var results = await Task.WhenAll(tasks);

            sw.Stop();

            var succeededCount = results.Count(r => r == 0);
            var failedCount = results.Count(r => r == -1);

            Assert.AreEqual(1, succeededCount, "There should be one and only one succeeded request");
            Assert.AreEqual(2, failedCount, "There should be two failed requests");
            Assert.IsTrue(sw.ElapsedMilliseconds < 50, "There should be no delays added");

            await Task.Delay(400);
        }
    }

    [TestMethod]
    public async Task NoDelaysWhenRateLimitNotExceeded()
    {
        string key = Guid.NewGuid().ToString();

        var store = new MemoryCacheCounterStore();

        var limiter = new LeakyBucketRateLimitMethod
        {
            PermitLimit = 3,
            IntervalInSeconds = 0.3,
        };

        for (int i = 0; i < 10; i++)
        {
            var sw = Stopwatch.StartNew();

            var result = await limiter.IsExceededAsync(key, 1, store, null);

            sw.Stop();

            Assert.AreEqual(2, result, "Request should succeed");
            Assert.IsTrue(sw.ElapsedMilliseconds < 50, "There should be no delays added");

            await Task.Delay(110);
        }
    }
}