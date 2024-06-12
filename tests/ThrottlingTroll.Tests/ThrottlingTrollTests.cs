using Moq;
using StackExchange.Redis;

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

            Assert.IsTrue(msg.StartsWith($"ThrottlingTroll failed to get its config. System.Exception: {errorMsg}"));
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

    class FailingRule : ThrottlingTrollRule
    {
        public string ErrorMessage = "Oops...";

        protected internal override Task<LimitCheckResult> IsExceededAsync(IHttpRequestProxy request, long cost, ICounterStore store, string configName, Action<LogLevel, string> log)
        {
            throw new Exception(this.ErrorMessage);
        }
    }

    class SucceedingRule : ThrottlingTrollRule
    {
        public LimitCheckResult? Result;

        protected internal override Task<LimitCheckResult> IsExceededAsync(IHttpRequestProxy request, long cost, ICounterStore store, string configName, Action<LogLevel, string> log)
        {
            this.Result = new LimitCheckResult(0, this, DateTimeOffset.Now.Millisecond, DateTimeOffset.Now.ToString());

            return Task.FromResult(this.Result);
        }
    }

    [TestMethod]
    public async Task IsExceededAsync_ThrowOnErrorIsFalse_MethodCompletes()
    {
        // Arrange

        var requestMock = new Mock<IIncomingHttpRequestProxy>();

        var succeedingRule = new SucceedingRule();
        var failingRule = new FailingRule { LimitMethod = new FixedWindowRateLimitMethod() { ShouldThrowOnFailures = false } };

        bool logWasCalled = false;

        var log = (LogLevel level, string msg) =>
        {
            logWasCalled = true;

            Assert.AreEqual(LogLevel.Error, level);

            Assert.IsTrue(msg.StartsWith($"ThrottlingTroll failed. System.Exception: {failingRule.ErrorMessage}"));
        };

        var troll = new ThrottlingTroll(log, new MemoryCacheCounterStore(), () =>
        {
            var rules = new List<ThrottlingTrollRule>
            {
                succeedingRule,
                failingRule,
            };

            return Task.FromResult(new ThrottlingTrollConfig { Rules = rules });
        },
        null, null);

        // Act

        var result = await troll.IsExceededAsync(requestMock.Object, new List<Func<Task>>());

        // Assert

        Assert.AreEqual(succeedingRule.Result, result.Single());

        Assert.IsTrue(logWasCalled);
    }

    [TestMethod]
    public async Task IsExceededAsync_ThrowOnErrorIsTrue_ExceptionThrown()
    {
        // Arrange

        var requestMock = new Mock<IIncomingHttpRequestProxy>();

        var succeedingRule = new SucceedingRule();
        var failingRule = new FailingRule { LimitMethod = new FixedWindowRateLimitMethod() { ShouldThrowOnFailures = true } };

        bool logWasCalled = false;

        var log = (LogLevel level, string msg) =>
        {
            logWasCalled = true;

            Assert.AreEqual(LogLevel.Error, level);

            Assert.IsTrue(msg.StartsWith($"ThrottlingTroll failed. System.Exception: {failingRule.ErrorMessage}"));
        };

        var troll = new ThrottlingTroll(log, new MemoryCacheCounterStore(), () =>
        {
            var rules = new List<ThrottlingTrollRule>
            {
                succeedingRule,
                failingRule,
            };

            return Task.FromResult(new ThrottlingTrollConfig { Rules = rules });
        },
        null, null);

        // Act

        Func<Task<List<LimitCheckResult>>> act = () => troll.IsExceededAsync(requestMock.Object, new List<Func<Task>>());

        // Assert

        var resultException = await Assert.ThrowsExceptionAsync<Exception>(act);

        Assert.AreEqual(failingRule.ErrorMessage, resultException.Message);

        Assert.IsTrue(logWasCalled);
    }

    [TestMethod]
    public async Task IsExceededAsync_TransientInternalError_AllRulesAreStillExecuted()
    {
        // Arrange

        var requestMock = new Mock<IIncomingHttpRequestProxy>();

        var failingRule = new FailingRule();
        var succeedingRule = new SucceedingRule();

        bool logWasCalled = false;

        var log = (LogLevel level, string msg) =>
        {
            logWasCalled = true;

            Assert.AreEqual(LogLevel.Error, level);

            Assert.IsTrue(msg.StartsWith($"ThrottlingTroll failed. System.Exception: {failingRule.ErrorMessage}"));
        };

        var troll = new ThrottlingTroll(log, new MemoryCacheCounterStore(), () =>
        {
            var rules = new List<ThrottlingTrollRule>
            {
                failingRule,
                succeedingRule
            };

            return Task.FromResult(new ThrottlingTrollConfig { Rules = rules });
        },
        null, null);

        // Act

        var result = await troll.IsExceededAsync(requestMock.Object, new List<Func<Task>>());

        // Assert

        Assert.AreEqual(succeedingRule.Result, result.Single());

        Assert.IsTrue(logWasCalled);
    }
}