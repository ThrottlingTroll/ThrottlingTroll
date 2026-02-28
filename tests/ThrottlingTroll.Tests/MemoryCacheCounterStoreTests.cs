
namespace ThrottlingTroll.Tests;

[TestClass]
public class MemoryCacheCounterStoreTests
{
    [TestMethod]
    public async Task IncrementAndGetAsyncIsThreadSafe()
    {
        // Arrange

        string key = Guid.NewGuid().ToString();
        var ttl = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(23456);
        int numOfTries = 256;

        // Act

        var store = new MemoryCacheCounterStore();

        Parallel.For(0, numOfTries, new ParallelOptions { MaxDegreeOfParallelism = numOfTries }, i => {

            store.IncrementAndGetAsync(key, 1, ttl.UtcTicks, CounterStoreIncrementAndGetOptions.SetAbsoluteTtl, 1, null).Wait();

        });

        long result = await store.GetAsync(key, null);

        // Assert

        Assert.AreEqual(numOfTries, result, "Looks like MemoryCacheCounterStore.IncrementAndGetAsync() is not thread-safe");
    }
}