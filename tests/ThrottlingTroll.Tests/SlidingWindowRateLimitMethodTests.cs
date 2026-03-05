using StackExchange.Redis;
using System.Diagnostics;
using System.Runtime.Caching;

namespace ThrottlingTroll.Tests;

[TestClass]
public class SlidingWindowRateLimitMethodTests
{
    [TestMethod]
    public async Task CreatesThreeAndOnlyThreeBuckets()
    {
        // Arrange

        string key = Guid.NewGuid().ToString();

        var store = new MemoryCacheCounterStore();

        var limiter = new SlidingWindowRateLimitMethod
        {
            IntervalInSeconds = 1.5,
            NumOfBuckets = 3
        };

        MemoryCache.Default.Flush();

        // Act

        // Five buckets will be created, but 2 of them should expire
        var stopAt = DateTime.UtcNow + TimeSpan.FromSeconds(2.6);
        while (DateTime.UtcNow < stopAt)
        {
            await limiter.IsExceededAsync(key, 1, store, null);
        }

        // Assert

        var items = MemoryCache.Default
            .Where(i => !i.Key.EndsWith("-exceeded"))
            .ToDictionary(i => i.Key, i => i.Value as MemoryCacheCounterStore.CacheEntry);

        Assert.AreEqual(3, items.Count, "There should be exactly three buckets created");

        Assert.IsTrue(items.ContainsKey($"{key}-0"));
        Assert.IsTrue(items.ContainsKey($"{key}-1"));
        Assert.IsTrue(items.ContainsKey($"{key}-2"));

        var minTtl = items.Values.Select(v => v.ExpiresAt).Min();
        var maxTtl = items.Values.Select(v => v.ExpiresAt).Max();

        Assert.AreEqual(1, Math.Round((maxTtl - minTtl).TotalSeconds), "Buckets should span a 1.5 seconds interval");

        if (minTtl.Millisecond == 500)
        {
            Assert.AreEqual(500, maxTtl.Millisecond, "Buckets should be aligned to the bucket size");
        }
        else
        {
            Assert.AreEqual(0, minTtl.Millisecond, "Buckets should be aligned to the bucket size");
            Assert.AreEqual(0, maxTtl.Millisecond, "Buckets should be aligned to the bucket size");
        }
    }

    [TestMethod]
    public async Task CountsAllBuckets()
    {
        // Arrange

        string key = Guid.NewGuid().ToString();

        var store = new MemoryCacheCounterStore();

        var limiter = new SlidingWindowRateLimitMethod
        {
            PermitLimit = 4,
            IntervalInSeconds = 2,
            NumOfBuckets = 2
        };

        MemoryCache.Default.Flush();

        var results = new List<int>();

        // Need to be at the beginning of current second, otherwise Assert section has a chance
        // to fall into 3-rd second and miss the first bucket
        int ms = DateTime.UtcNow.Millisecond;
        while (DateTime.UtcNow.Millisecond > ms)
        {
            await Task.Delay(10);
        }

        // Act

        Trace.WriteLine($"{DateTime.Now.ToString("o")} Making two requests");

        results.Add(await limiter.IsExceededAsync(key, 1, store, null));
        results.Add(await limiter.IsExceededAsync(key, 1, store, null));

        Trace.WriteLine($"{DateTime.Now.ToString("o")} Sleeping 1 sec");

        await Task.Delay(TimeSpan.FromMilliseconds(1000));

        Trace.WriteLine($"{DateTime.Now.ToString("o")} Making other two requests");

        results.Add(await limiter.IsExceededAsync(key, 1, store, null));
        results.Add(await limiter.IsExceededAsync(key, 1, store, null));

        Trace.WriteLine($"{DateTime.Now.ToString("o")} Making final request");

        int finalResult = await limiter.IsExceededAsync(key, 1, store, null);

        // Assert

        var items = MemoryCache.Default
            .Where(i => !i.Key.EndsWith("-exceeded"))
            .ToDictionary(i => i.Key, i => i.Value as MemoryCacheCounterStore.CacheEntry);

        Trace.WriteLine("Counts: " + String.Join(',', items.Values.Select(v => v.Count)));

        Assert.AreEqual(2, items.Count, "There should be exactly two buckets created");

        Assert.IsTrue(items.Values.Any(ke => ke.Count == 2));
        Assert.IsTrue(items.Values.Any(ke => ke.Count == 3));

        Assert.IsTrue(results.All(r => r >= 0), "First four requests should not exceed the limit");
        Assert.AreEqual(-1, finalResult, "Last fifth request should exceed the limit");
    }

    [TestMethod]
    public async Task LimitIsConstantlyExceeded()
    {
        // Arrange

        string key = Guid.NewGuid().ToString();

        var store = new MemoryCacheCounterStore();

        var limiter = new SlidingWindowRateLimitMethod
        {
            PermitLimit = 6,
            IntervalInSeconds = 2.4,
            NumOfBuckets = 3
        };

        MemoryCache.Default.Flush();

        var results = new List<Tuple<string, int>>();

        // Act

        var stopAt = DateTime.UtcNow + TimeSpan.FromSeconds(6);
        while (DateTime.UtcNow < stopAt)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250));

            results.Add(new Tuple<string, int>(DateTime.UtcNow.ToString("o"), await limiter.IsExceededAsync(key, 1, store, null)));
        }

        // Assert

        Trace.WriteLine(string.Join('\n', results.Select(r => $"{r.Item1}|{r.Item2}")));

        var items = MemoryCache.Default
            .Where(i => !i.Key.EndsWith("-exceeded"))
            .ToDictionary(i => i.Key, i => i.Value as MemoryCacheCounterStore.CacheEntry);

        Assert.AreEqual(3, items.Count, "There should be exactly three buckets created");

        var firstSixResults = results.Take(6);
        Assert.IsTrue(firstSixResults.All(r => r.Item2 >= 0), "First six requests should not exceed the limit");

        var otherResults = results.Skip(6);
        Assert.IsTrue(otherResults.All(r => r.Item2 == -1), "Requests starting from the seventh should exceed the limit");
    }

    [TestMethod]
    public async Task SlidingWindowWithOneBucketBehavesSameAsFixedWindow()
    {
        string key = Guid.NewGuid().ToString();

        var store = new MemoryCacheCounterStore();

        var limiter = new SlidingWindowRateLimitMethod
        {
            PermitLimit = 3,
            IntervalInSeconds = 1,
            NumOfBuckets = 1,
        };

        // If too close to the end of current second, need to wait
        if (DateTime.UtcNow.Millisecond > 800)
        {
            Thread.Sleep(200);
        }

        Trace.WriteLine($"{DateTime.Now.ToString("o")} Started");

        for (int i = 0; i < limiter.PermitLimit; i++)
        {
            Assert.AreNotEqual(-1, await limiter.IsExceededAsync(key, 1, store, null));
        }

        // Now we should exceed
        Assert.AreEqual(-1, await limiter.IsExceededAsync(key, 1, store, null));
        Assert.AreEqual(-1, await limiter.IsExceededAsync(key, 1, store, null));
        Assert.AreEqual(-1, await limiter.IsExceededAsync(key, 1, store, null));

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
            Assert.AreNotEqual(-1, await limiter.IsExceededAsync(key, 1, store, null));
        }

        // Now we should exceed again
        Assert.AreEqual(-1, await limiter.IsExceededAsync(key, 1, store, null));

        Trace.WriteLine($"{DateTime.Now.ToString("o")} Finished");
    }


    [TestMethod]
    public async Task SlidingWindowWithTwoBucketsIsResetAfterTwoSeconds()
    {
        string key = Guid.NewGuid().ToString();

        var store = new MemoryCacheCounterStore();

        var limiter = new SlidingWindowRateLimitMethod
        {
            PermitLimit = 3,
            IntervalInSeconds = 2,
            NumOfBuckets = 2,
        };

        // If too close to the end of current second, need to wait
        if (DateTime.UtcNow.Millisecond > 800)
        {
            Thread.Sleep(200);
        }

        Trace.WriteLine($"{DateTime.Now.ToString("o")} Started");

        for (int i = 0; i < limiter.PermitLimit; i++)
        {
            Assert.AreNotEqual(-1, await limiter.IsExceededAsync(key, 1, store, null));
        }

        // Now we should exceed
        Assert.AreEqual(-1, await limiter.IsExceededAsync(key, 1, store, null));
        Assert.AreEqual(-1, await limiter.IsExceededAsync(key, 1, store, null));
        Assert.AreEqual(-1, await limiter.IsExceededAsync(key, 1, store, null));

        // Now waiting for two seconds
        Trace.WriteLine($"{DateTime.Now.ToString("o")} Waiting");

        Thread.Sleep(1000);

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
            Assert.AreNotEqual(-1, await limiter.IsExceededAsync(key, 1, store, null));
        }

        // Now we should exceed again
        Assert.AreEqual(-1, await limiter.IsExceededAsync(key, 1, store, null));

        Trace.WriteLine($"{DateTime.Now.ToString("o")} Finished");
    }
}