using Moq;

namespace ThrottlingTroll.Tests;

[TestClass]
public class ThrottlingTrollCoreTests
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

        var troll = new ThrottlingTrollCore(log, new MemoryCacheCounterStore(), async () =>
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

        var troll = new ThrottlingTrollCore(log, new MemoryCacheCounterStore(), () =>
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

        var troll = new ThrottlingTrollCore(log, new MemoryCacheCounterStore(), () =>
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

        var troll = new ThrottlingTrollCore(log, new MemoryCacheCounterStore(), () =>
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

    [TestMethod]
    public async Task CheckAndBreakTheCircuit_ThrowingGetConfigFunc_LogsExceptionAndReturns()
    {
        // Arrange

        string errorMsg = "Ooops...";

        bool logWasCalled = false;

        var log = (LogLevel level, string msg) =>
        {
            logWasCalled = true;

            Assert.AreEqual(LogLevel.Error, level);

            Assert.IsTrue(msg.StartsWith($"ThrottlingTroll failed to get its config. System.Exception: {errorMsg}"));
        };

        var troll = new ThrottlingTrollCore(log, new MemoryCacheCounterStore(), async () =>
        {
            throw new Exception(errorMsg);
        },
        null, null);

        // Act

        await troll.CheckAndBreakTheCircuit(null, null, null);

        // Assert

        Assert.IsTrue(logWasCalled);
    }

    [TestMethod]
    public async Task CheckAndBreakTheCircuit_EmptyConfig_DoesNothing()
    {
        // Arrange

        var troll = new ThrottlingTrollCore(null, new MemoryCacheCounterStore(), async () =>
        {
            return new ThrottlingTrollConfig();
        },
        null, null);

        // Act

        await troll.CheckAndBreakTheCircuit(null, null, null);

        // Assert
    }

    class RuleWithFailingGetUniqueCacheKeyMethod : ThrottlingTrollRule
    {
        protected internal override string GetUniqueCacheKey(IHttpRequestProxy request, string configName)
        {
            throw new Exception("Oops...");
        }
    }

    [TestMethod]
    public async Task CheckAndBreakTheCircuit_NoCircuitBreakerRules_DoesNothing()
    {
        // Arrange

        var troll = new ThrottlingTrollCore(null, new MemoryCacheCounterStore(), async () =>
        {
            return new ThrottlingTrollConfig 
            {
                Rules = new List<ThrottlingTrollRule>
                {
                    new RuleWithFailingGetUniqueCacheKeyMethod()
                }
            };
        },
        null, null);

        // Act

        await troll.CheckAndBreakTheCircuit(null, null, null);

        // Assert
    }

    [TestMethod]
    public async Task CheckAndBreakTheCircuit_FailureLimitNotExceeded_DoesNothing()
    {
        // Arrange

        var rule = new ThrottlingTrollRule
        {
            LimitMethod = new CircuitBreakerRateLimitMethod
            {
                PermitLimit = 3,
                IntervalInSeconds = 1,
                TrialIntervalInSeconds = 1,
            }
        };

        var troll = new ThrottlingTrollCore(null, new MemoryCacheCounterStore(), async () =>
        {
            return new ThrottlingTrollConfig
            {
                Rules = new List<ThrottlingTrollRule>
                {
                    rule
                }
            };
        },
        null, null);

        var requestMock = new Mock<IIncomingHttpRequestProxy>();

        var responseMock = new Mock<IHttpResponseProxy>();

        responseMock.SetupGet(r => r.StatusCode).Returns(500);

        // Act

        await troll.CheckAndBreakTheCircuit(requestMock.Object, responseMock.Object, null);

        // Assert

        string uniqueCacheKey = rule.GetUniqueCacheKey(requestMock.Object, "");
        bool isUnderTrial = CircuitBreakerRateLimitMethod.IsUnderTrial(uniqueCacheKey);

        Assert.IsFalse(isUnderTrial);
    }

    [TestMethod]
    public async Task CheckAndBreakTheCircuit_FailureLimitExceeded_PutsToTrialMode()
    {
        // Arrange

        var rule = new ThrottlingTrollRule
        {
            LimitMethod = new CircuitBreakerRateLimitMethod
            {
                PermitLimit = 0,
                IntervalInSeconds = 1,
                TrialIntervalInSeconds = 1,
            }
        };

        var troll = new ThrottlingTrollCore(null, new MemoryCacheCounterStore(), async () =>
        {
            return new ThrottlingTrollConfig
            {
                Rules = new List<ThrottlingTrollRule>
                {
                    rule
                }
            };
        },
        null, null);

        var requestMock = new Mock<IIncomingHttpRequestProxy>();

        var responseMock = new Mock<IHttpResponseProxy>();

        responseMock.SetupGet(r => r.StatusCode).Returns(500);

        // Act

        await troll.CheckAndBreakTheCircuit(requestMock.Object, responseMock.Object, null);

        // Assert

        string uniqueCacheKey = rule.GetUniqueCacheKey(requestMock.Object, "");
        bool isUnderTrial = CircuitBreakerRateLimitMethod.IsUnderTrial(uniqueCacheKey);

        Assert.IsTrue(isUnderTrial);
    }

    [TestMethod]
    public async Task CheckAndBreakTheCircuit_TrialRequestSucceeds_ReleasesFromTrialMode()
    {
        // Arrange

        var rule = new ThrottlingTrollRule
        {
            LimitMethod = new CircuitBreakerRateLimitMethod
            {
                PermitLimit = 3,
                IntervalInSeconds = 1,
                TrialIntervalInSeconds = 1,
            }
        };

        var troll = new ThrottlingTrollCore(null, new MemoryCacheCounterStore(), async () =>
        {
            return new ThrottlingTrollConfig
            {
                Rules = new List<ThrottlingTrollRule>
                {
                    rule
                }
            };
        },
        null, null);

        var requestMock = new Mock<IIncomingHttpRequestProxy>();

        var responseMock = new Mock<IHttpResponseProxy>();

        responseMock.SetupGet(r => r.StatusCode).Returns(200);

        string uniqueCacheKey = rule.GetUniqueCacheKey(requestMock.Object, "");

        CircuitBreakerRateLimitMethod.PutIntoTrial(uniqueCacheKey);

        // Act

        await troll.CheckAndBreakTheCircuit(requestMock.Object, responseMock.Object, null);

        // Assert

        bool isUnderTrial = CircuitBreakerRateLimitMethod.IsUnderTrial(uniqueCacheKey);

        Assert.IsFalse(isUnderTrial);
    }

}