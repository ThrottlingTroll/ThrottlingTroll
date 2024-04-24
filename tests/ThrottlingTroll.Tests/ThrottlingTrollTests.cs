using Moq;

namespace ThrottlingTroll.Tests;

[TestClass]
public class ThrottlingTrollTests
{
    [TestMethod]
    public async Task IsIngressOrEgressExceededAsync_ThrowingGetConfigFunc_LogsExceptionAndContinues()
    {
        // Arrange

        var requestMock = new Mock<IIncomingHttpRequestProxy>();

        string errorMsg = "Ooops...";

        bool logWasCalled = false;

        var log = (LogLevel level, string msg) => 
        {
            logWasCalled = true;

            Assert.AreEqual(LogLevel.Error, level);

            Assert.IsTrue(msg.StartsWith($"ThrottlingTroll failed. System.Exception: {errorMsg}"));
        };

        var troll = new ThrottlingTroll(log, new MemoryCacheCounterStore(), async () =>
        {
            throw new Exception(errorMsg);
        },
        null, null);

        bool nextWasCalled = false;

        // Act

        var result = await troll.IsIngressOrEgressExceededAsync(requestMock.Object, null, async () => { 

            nextWasCalled = true;
        });

        // Assert

        Assert.AreEqual(0, result.Count);
        Assert.IsTrue(logWasCalled);
        Assert.IsTrue(nextWasCalled);
    }
}