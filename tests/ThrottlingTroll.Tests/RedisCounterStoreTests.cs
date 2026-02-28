using Moq;
using StackExchange.Redis;
using ThrottlingTroll.CounterStores.Redis;

namespace ThrottlingTroll.Tests;

[TestClass]
public class RedisCounterStoreTests
{
    [TestMethod]
    public async Task GetAsyncSucceedes()
    {
        // Arrange

        string key = Guid.NewGuid().ToString();
        long val = 123456789;

        var redisDbMock = new Mock<IDatabase>();

        redisDbMock
            .Setup(d => d.StringGetAsync(key, It.IsAny<CommandFlags>()))
            .Returns(Task.FromResult((RedisValue)val));

        var redisMock = new Mock<IConnectionMultiplexer>();

        redisMock
            .Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(redisDbMock.Object);

        // Act

        var store = new RedisCounterStore(redisMock.Object);

        long result = await store.GetAsync(key, null);

        // Assert

        Assert.AreEqual(val, result);
    }

    [TestMethod]
    public async Task IncrementAndGetAsyncSucceedes()
    {
        // Arrange

        string key = Guid.NewGuid().ToString();
        long val = 123456789;
        var ttl = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(23456);

        var redisDbMock = new Mock<IDatabase>();

        redisDbMock
            .Setup(d => d.ScriptEvaluateAsync(
                It.IsAny<LuaScript>(), 
                It.IsAny<object>(), 
                CommandFlags.None)
            )
            .Returns(Task.FromResult(RedisResult.Create(val)));

        var redisMock = new Mock<IConnectionMultiplexer>();

        redisMock
            .Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(redisDbMock.Object);

        // Act

        var store = new RedisCounterStore(redisMock.Object);

        long result = await store.IncrementAndGetAsync(key, 1, ttl.UtcTicks, CounterStoreIncrementAndGetOptions.SetAbsoluteTtl, 1, null);

        // Assert

        Assert.AreEqual(val, result);
    }
}