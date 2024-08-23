using Moq;
using System.Diagnostics;

namespace ThrottlingTroll.Tests;

[TestClass]
public class CircuitBreakerRateLimitMethodTests
{
    [TestMethod]
    public async Task IsExceededAsyncDoesTheJob()
    {
        string key = Guid.NewGuid().ToString();

        var store = new MemoryCacheCounterStore();

        var limiter = new CircuitBreakerRateLimitMethod
        {
            PermitLimit = 3,
            TrialIntervalInSeconds = 1,
        };

        // Placing this limit into trial mode
        CircuitBreakerRateLimitMethod.PutIntoTrial(key);

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
    public void IsFailed_EverythingIsNull_DoesNothing()
    {
        // Arrange

        var limiter = new CircuitBreakerRateLimitMethod();

        // Act

        bool result = limiter.IsFailed(null, null);

        // Assert

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsFailed_ExceptionIsNull_FalseIfResponseSuccessful()
    {
        // Arrange

        var limiter = new CircuitBreakerRateLimitMethod()
        {
            IntervalInSeconds = 1,
            TrialIntervalInSeconds = 2
        };

        var responseProxyMock = new Mock<IHttpResponseProxy>();

        responseProxyMock.SetupGet(m => m.StatusCode).Returns(200);

        // Act

        bool result = limiter.IsFailed(responseProxyMock.Object, null);

        // Assert

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsFailed_ExceptionIsNull_TrueIfResponseFailed()
    {
        // Arrange

        var limiter = new CircuitBreakerRateLimitMethod()
        {
            IntervalInSeconds = 1,
            TrialIntervalInSeconds = 2
        };

        var responseProxyMock = new Mock<IHttpResponseProxy>();

        responseProxyMock.SetupGet(m => m.StatusCode).Returns(400);

        // Act

        bool result = limiter.IsFailed(responseProxyMock.Object, null);

        // Assert

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsFailed_ResponseIsNull_TrueOnException()
    {
        // Arrange

        var limiter = new CircuitBreakerRateLimitMethod()
        {
            IntervalInSeconds = 1,
            TrialIntervalInSeconds = 2
        };

        // Act

        bool result = limiter.IsFailed(null, new Exception());

        // Assert

        Assert.IsTrue(result);
    }
}