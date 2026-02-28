using Microsoft.Net.Http.Headers;
using Moq;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace ThrottlingTroll.Tests;

[TestClass]
public class ThrottlingTrollHandlerTests
{
    class ThrottlingTrollHandler_Accessor : ThrottlingTrollHandler
    {
        public ThrottlingTrollHandler_Accessor(
            Func<List<LimitCheckResult>, IHttpRequestProxy, IHttpResponseProxy, CancellationToken, Task> responseFabric,
            ICounterStore counterStore, 
            ThrottlingTrollEgressConfig options

        ) : base(responseFabric, counterStore, options)
        {
        }

        public HttpResponseMessage Send(HttpRequestMessage request)
        {
            return base.Send(request, CancellationToken.None);
        }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        {
            return base.SendAsync(request, CancellationToken.None);
        }
    }

    private const string LocalHost = "http://localhost:17345";

    [TestMethod]
    public async Task SendAsyncSuccessfullySendsRequest()
    {
        // Arrange

        using var app = WebApplication.Create();
        app.Urls.Add(LocalHost);
        app.MapGet("/", () => Results.StatusCode((int)HttpStatusCode.PaymentRequired));
        await app.StartAsync();

        var handler = new ThrottlingTrollHandler_Accessor(null, null, new ThrottlingTrollEgressConfig());

        var request = new HttpRequestMessage(HttpMethod.Get, LocalHost);

        // Act

        var result = await handler.SendAsync(request);

        // Assert

        Assert.AreEqual(HttpStatusCode.PaymentRequired, result.StatusCode);
    }

    [TestMethod]
    public async Task SendAsyncReturns429TooManyRequests()
    {
        // Arrange

        var counterStoreMock = new Mock<ICounterStore>();

        counterStoreMock
            .Setup(s => s.IncrementAndGetAsync(It.IsAny<string>(), 1L, It.IsAny<long>(), CounterStoreIncrementAndGetOptions.SetAbsoluteTtl, 1L, It.IsAny<IHttpRequestProxy>()))
            .Returns(Task.FromResult(2L));

        var options = new ThrottlingTrollEgressConfig
        {
            Rules = new []
            {
                new ThrottlingTrollRule
                {
                    LimitMethod = new FixedWindowRateLimitMethod
                    {
                        PermitLimit = 1,
                        IntervalInSeconds = 1
                    }
                }
            }
        };

        var handler = new ThrottlingTrollHandler_Accessor(null, counterStoreMock.Object, options);

        var request = new HttpRequestMessage();

        // Act

        var result = await handler.SendAsync(request);

        // Assert

        Assert.AreEqual(HttpStatusCode.TooManyRequests, result.StatusCode);
        Assert.AreEqual(TimeSpan.FromSeconds(1), result.Headers.RetryAfter.Delta);
        Assert.AreEqual("Retry after 1 seconds", await result.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task SendAsyncPropagatesExceptionToIngress()
    {
        // Arrange

        var counterStoreMock = new Mock<ICounterStore>();

        counterStoreMock
            .Setup(s => s.IncrementAndGetAsync(It.IsAny<string>(), 1L, It.IsAny<long>(), CounterStoreIncrementAndGetOptions.SetAbsoluteTtl, 1L, It.IsAny<IHttpRequestProxy>()))
            .Returns(Task.FromResult(2L));

        var options = new ThrottlingTrollEgressConfig
        {
            Rules = new[]
            {
                new ThrottlingTrollRule
                {
                    LimitMethod = new FixedWindowRateLimitMethod
                    {
                        PermitLimit = 1,
                        IntervalInSeconds = 1
                    }
                }
            },

            PropagateToIngress = true
        };

        var handler = new ThrottlingTrollHandler_Accessor(null, counterStoreMock.Object, options);

        var request = new HttpRequestMessage();

        // Assert

        await Assert.ThrowsExceptionAsync<ThrottlingTrollTooManyRequestsException>(() => handler.SendAsync(request));
    }

    [TestMethod]
    public async Task SendAsyncReturnsCustomResponse()
    {
        // Arrange

        var counterStoreMock = new Mock<ICounterStore>();

        counterStoreMock
            .Setup(s => s.IncrementAndGetAsync(It.IsAny<string>(), 1L, It.IsAny<long>(), CounterStoreIncrementAndGetOptions.SetAbsoluteTtl, 1L, It.IsAny<IHttpRequestProxy>()))
            .Returns(Task.FromResult(2L));

        var options = new ThrottlingTrollEgressConfig
        {
            Rules = new[]
            {
                new ThrottlingTrollRule
                {
                    LimitMethod = new FixedWindowRateLimitMethod
                    {
                        PermitLimit = 1,
                        IntervalInSeconds = 1
                    }
                }
            }
        };

        var retryAfterDateTime = DateTimeOffset.UtcNow + TimeSpan.FromDays(123);

        var handler = new ThrottlingTrollHandler_Accessor(

            async (limitExceededResult, httpRequestProxy, httpResponseProxy, cancellationToken) =>
            {
                httpResponseProxy.StatusCode = StatusCodes.Status500InternalServerError;
                httpResponseProxy.SetHttpHeader(HeaderNames.RetryAfter, new RetryConditionHeaderValue(retryAfterDateTime).ToString());
                await httpResponseProxy.WriteAsync($"Come back at {retryAfterDateTime}");
            },
            
            counterStoreMock.Object, options
        );

        var request = new HttpRequestMessage();

        // Act

        var result = await handler.SendAsync(request);

        // Assert

        Assert.AreEqual(HttpStatusCode.InternalServerError, result.StatusCode);
        Assert.AreEqual(retryAfterDateTime.ToString(), result.Headers.RetryAfter.Date.ToString());
        Assert.AreEqual($"Come back at {retryAfterDateTime}", await result.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task SendAsyncDoesRetries()
    {
        // Arrange

        var counterStoreMock = new Mock<ICounterStore>();

        counterStoreMock
            .Setup(s => s.IncrementAndGetAsync(It.IsAny<string>(), 1L, It.IsAny<long>(), CounterStoreIncrementAndGetOptions.SetAbsoluteTtl, 1L, It.IsAny<IHttpRequestProxy>()))
            .Returns(Task.FromResult(2L));

        var options = new ThrottlingTrollEgressConfig
        {
            Rules = new[]
            {
                new ThrottlingTrollRule
                {
                    LimitMethod = new FixedWindowRateLimitMethod
                    {
                        PermitLimit = 1,
                        IntervalInSeconds = 1
                    }
                }
            }
        };

        var handler = new ThrottlingTrollHandler_Accessor(

            async (limitExceededResult, httpRequestProxy, httpResponseProxy, cancellationToken) =>
            {
                var egressResponse = (IEgressHttpResponseProxy)httpResponseProxy;
                egressResponse.ShouldRetry = egressResponse.RetryCount < 2;

                egressResponse.SetHttpHeader(HeaderNames.RetryAfter, new RetryConditionHeaderValue(TimeSpan.FromSeconds(1)).ToString());
            },

            counterStoreMock.Object, options
        );

        var request = new HttpRequestMessage();

        // Act

        var sw = new Stopwatch();
        sw.Start();

        var result = await handler.SendAsync(request);

        sw.Stop();

        // Assert

        Assert.IsTrue(sw.Elapsed >= TimeSpan.FromMilliseconds(1950), $"Should take 2 seconds, but took ${sw.Elapsed}");

        Assert.AreEqual(HttpStatusCode.TooManyRequests, result.StatusCode);
        Assert.AreEqual(TimeSpan.FromSeconds(1), result.Headers.RetryAfter.Delta);
        Assert.AreEqual("Retry after 1 seconds", await result.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task SendSuccessfullySendsRequest()
    {
        // Arrange

        using var app = WebApplication.Create();
        app.Urls.Add(LocalHost);
        app.MapGet("/", () => Results.StatusCode((int)HttpStatusCode.PaymentRequired));
        await app.StartAsync();

        var handler = new ThrottlingTrollHandler_Accessor(null, null, new ThrottlingTrollEgressConfig());

        var request = new HttpRequestMessage(HttpMethod.Get, LocalHost);

        // Act

        var result = handler.Send(request);

        // Assert

        Assert.AreEqual(HttpStatusCode.PaymentRequired, result.StatusCode);
    }

    [TestMethod]
    public async Task SendReturns429TooManyRequests()
    {
        // Arrange

        var counterStoreMock = new Mock<ICounterStore>();

        counterStoreMock
            .Setup(s => s.IncrementAndGetAsync(It.IsAny<string>(), 1L, It.IsAny<long>(), CounterStoreIncrementAndGetOptions.SetAbsoluteTtl, 1L, It.IsAny<IHttpRequestProxy>()))
            .Returns(Task.FromResult(2L));

        var options = new ThrottlingTrollEgressConfig
        {
            Rules = new[]
            {
                new ThrottlingTrollRule
                {
                    LimitMethod = new FixedWindowRateLimitMethod
                    {
                        PermitLimit = 1,
                        IntervalInSeconds = 1
                    }
                }
            }
        };

        var handler = new ThrottlingTrollHandler_Accessor(null, counterStoreMock.Object, options);

        var request = new HttpRequestMessage();

        // Act

        var result = handler.Send(request);

        // Assert

        Assert.AreEqual(HttpStatusCode.TooManyRequests, result.StatusCode);
        Assert.AreEqual(TimeSpan.FromSeconds(1), result.Headers.RetryAfter.Delta);
        Assert.AreEqual("Retry after 1 seconds", await result.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public void SendPropagatesExceptionToIngress()
    {
        // Arrange

        var counterStoreMock = new Mock<ICounterStore>();

        counterStoreMock
            .Setup(s => s.IncrementAndGetAsync(It.IsAny<string>(), 1L, It.IsAny<long>(), CounterStoreIncrementAndGetOptions.SetAbsoluteTtl, 1L, It.IsAny<IHttpRequestProxy>()))
            .Returns(Task.FromResult(2L));

        var options = new ThrottlingTrollEgressConfig
        {
            Rules = new[]
            {
                new ThrottlingTrollRule
                {
                    LimitMethod = new FixedWindowRateLimitMethod
                    {
                        PermitLimit = 1,
                        IntervalInSeconds = 1
                    }
                }
            },

            PropagateToIngress = true
        };

        var handler = new ThrottlingTrollHandler_Accessor(null, counterStoreMock.Object, options);

        var request = new HttpRequestMessage();

        // Assert

        Assert.ThrowsException<ThrottlingTrollTooManyRequestsException>(() => handler.Send(request));
    }

    [TestMethod]
    public async Task SendReturnsCustomResponse()
    {
        // Arrange

        var counterStoreMock = new Mock<ICounterStore>();

        counterStoreMock
            .Setup(s => s.IncrementAndGetAsync(It.IsAny<string>(), 1L, It.IsAny<long>(), CounterStoreIncrementAndGetOptions.SetAbsoluteTtl, 1L, It.IsAny<IHttpRequestProxy>()))
            .Returns(Task.FromResult(2L));

        var options = new ThrottlingTrollEgressConfig
        {
            Rules = new[]
            {
                new ThrottlingTrollRule
                {
                    LimitMethod = new FixedWindowRateLimitMethod
                    {
                        PermitLimit = 1,
                        IntervalInSeconds = 1
                    }
                }
            }
        };

        var retryAfterDateTime = DateTimeOffset.UtcNow + TimeSpan.FromDays(123);

        var handler = new ThrottlingTrollHandler_Accessor(

            async (limitExceededResult, httpRequestProxy, httpResponseProxy, cancellationToken) =>
            {
                httpResponseProxy.StatusCode = (int)HttpStatusCode.InternalServerError;
                httpResponseProxy.SetHttpHeader(HeaderNames.RetryAfter, new RetryConditionHeaderValue(retryAfterDateTime).ToString());
                await httpResponseProxy.WriteAsync($"Come back at {retryAfterDateTime}");
            },

            counterStoreMock.Object, options
        );

        var request = new HttpRequestMessage();

        // Act

        var result = handler.Send(request);

        // Assert

        Assert.AreEqual(HttpStatusCode.InternalServerError, result.StatusCode);
        Assert.AreEqual(retryAfterDateTime.ToString(), result.Headers.RetryAfter.Date.ToString());
        Assert.AreEqual($"Come back at {retryAfterDateTime}", await result.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task SendDoesRetries()
    {
        // Arrange

        var counterStoreMock = new Mock<ICounterStore>();

        counterStoreMock
            .Setup(s => s.IncrementAndGetAsync(It.IsAny<string>(), 1L, It.IsAny<long>(), CounterStoreIncrementAndGetOptions.SetAbsoluteTtl, 1L, It.IsAny<IHttpRequestProxy>()))
            .Returns(Task.FromResult(2L));

        var options = new ThrottlingTrollEgressConfig
        {
            Rules = new[]
            {
                new ThrottlingTrollRule
                {
                    LimitMethod = new FixedWindowRateLimitMethod
                    {
                        PermitLimit = 1,
                        IntervalInSeconds = 1
                    }
                }
            }
        };

        var handler = new ThrottlingTrollHandler_Accessor(

            async (limitExceededResult, httpRequestProxy, httpResponseProxy, cancellationToken) =>
            {
                var egressResponse = (IEgressHttpResponseProxy)httpResponseProxy;
                egressResponse.ShouldRetry = egressResponse.RetryCount < 2;

                egressResponse.SetHttpHeader(HeaderNames.RetryAfter, new RetryConditionHeaderValue(TimeSpan.FromSeconds(1)).ToString());
            },

            counterStoreMock.Object, options
        );

        var request = new HttpRequestMessage();

        // Act

        var sw = new Stopwatch();
        sw.Start();

        var result = handler.Send(request);

        sw.Stop();

        // Assert

        Assert.IsTrue(sw.Elapsed > TimeSpan.FromSeconds(2));

        Assert.AreEqual(HttpStatusCode.TooManyRequests, result.StatusCode);
        Assert.AreEqual(TimeSpan.FromSeconds(1), result.Headers.RetryAfter.Delta);
        Assert.AreEqual("Retry after 1 seconds", await result.Content.ReadAsStringAsync());
    }
}