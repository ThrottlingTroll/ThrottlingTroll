using Moq;
using StackExchange.Redis;

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

        long result = await store.GetAsync(key);

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
            .Setup(d => d.StringIncrementAsync(key, It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .Returns(Task.FromResult(val));

        redisDbMock
            .Setup(d => d.KeyExpireAsync(key, It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
            .Callback((RedisKey key, TimeSpan? expiry, ExpireWhen when, CommandFlags flags) => {

                Assert.AreEqual(expiry, ttl);
                Assert.AreEqual(when, ExpireWhen.HasNoExpiry);
            });

        var redisMock = new Mock<IConnectionMultiplexer>();

        redisMock
            .Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(redisDbMock.Object);

        // Act

        var store = new RedisCounterStore(redisMock.Object);

        long result = await store.IncrementAndGetAsync(key, ttl, false);

        // Assert

        Assert.AreEqual(val, result);
    }
}