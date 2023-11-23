using Azure;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Net.Http.Headers;
using Moq;

namespace ThrottlingTroll.AzureFunctionsAspNet.Tests
{

    class FakeFunctionContext : FunctionContext
    {
        public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>() { { "HttpRequestContext", new DefaultHttpContext() } };

        public override string InvocationId => throw new NotImplementedException();

        public override string FunctionId => throw new NotImplementedException();

        public override TraceContext TraceContext => throw new NotImplementedException();

        public override BindingContext BindingContext => throw new NotImplementedException();

        public override RetryContext RetryContext => throw new NotImplementedException();

        public override IServiceProvider InstanceServices { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public override FunctionDefinition FunctionDefinition => throw new NotImplementedException();

        public override IInvocationFeatures Features => throw new NotImplementedException();
    }

    [TestClass]
    public class ThrottlingTrollMiddlewareTests
    {
        [TestMethod]
        public async Task InvokeAsyncTest_NoLimits_DoesNothing()
        {
            // Arrange 

            var counterStoreMock = new Mock<ICounterStore>();

            var config = new ThrottlingTrollConfig();

            var options = new ThrottlingTrollOptions
            {
                CounterStore = counterStoreMock.Object,
                GetConfigFunc = () => Task.FromResult(config)
            };

            bool nextWasCalled = false;

            var middleware = new ThrottlingTrollMiddleware(options);

            // Act

            await middleware.Invoke(new FakeFunctionContext(), async () =>
            {
                Assert.IsFalse(nextWasCalled);

                nextWasCalled = true;

            });

            // Assert

            Assert.IsTrue(nextWasCalled);
        }

        [TestMethod]
        public async Task InvokeAsyncTest_NoLimits_BubblesUpException()
        {
            // Arrange 

            var counterStoreMock = new Mock<ICounterStore>();

            var config = new ThrottlingTrollConfig();

            var options = new ThrottlingTrollOptions
            {
                CounterStore = counterStoreMock.Object,
                GetConfigFunc = () => Task.FromResult(config)
            };

            var exception = new Exception("Oops");

            var middleware = new ThrottlingTrollMiddleware(options);

            // Act

            Func<Task> act = () => middleware.Invoke(new FakeFunctionContext(), async () =>
            {
                throw exception;

            });

            // Assert

            var resultException = await Assert.ThrowsExceptionAsync<Exception>(act);

            Assert.AreEqual(exception, resultException);
        }

        [TestMethod]
        public async Task InvokeAsyncTest_EgressThrowsTooManyRequestsException_ReturnsRetryAfterResponse()
        {
            // Arrange 

            var counterStoreMock = new Mock<ICounterStore>();

            var config = new ThrottlingTrollConfig();

            var options = new ThrottlingTrollOptions
            {
                CounterStore = counterStoreMock.Object,
                GetConfigFunc = () => Task.FromResult(config)
            };

            var exception = new ThrottlingTrollTooManyRequestsException
            {
                RetryAfterHeaderValue = DateTimeOffset.UtcNow.ToString("R")
            };

            var middleware = new ThrottlingTrollMiddleware(options);

            var functionContext = new FakeFunctionContext();
            var httpContext = functionContext.GetHttpContext()!;
            httpContext.Response.Body = new MemoryStream();

            // Act

            await middleware.Invoke(functionContext, async () =>
            {
                throw exception;

            });

            // Assert

            Assert.AreEqual(StatusCodes.Status429TooManyRequests, httpContext.Response.StatusCode);
            Assert.AreEqual(exception.RetryAfterHeaderValue, httpContext.Response.Headers[HeaderNames.RetryAfter].ToString());

            httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
            using (var reader = new StreamReader(httpContext.Response.Body))
            {
                Assert.AreEqual($"Retry after {exception.RetryAfterHeaderValue}", reader.ReadToEnd());
            }
        }

        [TestMethod]
        public async Task InvokeAsyncTest_EgressThrowsAggregateException_ReturnsRetryAfterResponse()
        {
            // Arrange 

            var counterStoreMock = new Mock<ICounterStore>();

            var config = new ThrottlingTrollConfig();

            var options = new ThrottlingTrollOptions
            {
                CounterStore = counterStoreMock.Object,
                GetConfigFunc = () => Task.FromResult(config)
            };

            var exception = new ThrottlingTrollTooManyRequestsException
            {
                RetryAfterHeaderValue = DateTimeOffset.UtcNow.ToString("R")
            };

            var middleware = new ThrottlingTrollMiddleware(options);

            var functionContext = new FakeFunctionContext();
            var httpContext = functionContext.GetHttpContext()!;
            httpContext.Response.Body = new MemoryStream();

            // Act

            await middleware.Invoke(functionContext, async () =>
            {
                Task.Run(() => { throw exception; }).Wait();

            });

            // Assert

            Assert.AreEqual(StatusCodes.Status429TooManyRequests, httpContext.Response.StatusCode);
            Assert.AreEqual(exception.RetryAfterHeaderValue, httpContext.Response.Headers[HeaderNames.RetryAfter].ToString());

            httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
            using (var reader = new StreamReader(httpContext.Response.Body))
            {
                Assert.AreEqual($"Retry after {exception.RetryAfterHeaderValue}", reader.ReadToEnd());
            }
        }

        [TestMethod]
        public async Task InvokeAsyncTest_EgressThrowsTooManyRequestsExceptionWithRetryAfterInSeconds_ReturnsRetryAfterResponse()
        {
            // Arrange 

            var counterStoreMock = new Mock<ICounterStore>();

            var config = new ThrottlingTrollConfig();

            var options = new ThrottlingTrollOptions
            {
                CounterStore = counterStoreMock.Object,
                GetConfigFunc = () => Task.FromResult(config)
            };

            var exception = new ThrottlingTrollTooManyRequestsException
            {
                RetryAfterHeaderValue = DateTimeOffset.UtcNow.Second.ToString()
            };

            var middleware = new ThrottlingTrollMiddleware(options);

            var functionContext = new FakeFunctionContext();
            var httpContext = functionContext.GetHttpContext()!;
            httpContext.Response.Body = new MemoryStream();

            // Act

            await middleware.Invoke(functionContext, async () =>
            {
                throw exception;

            });

            // Assert

            Assert.AreEqual(StatusCodes.Status429TooManyRequests, httpContext.Response.StatusCode);
            Assert.AreEqual(exception.RetryAfterHeaderValue, httpContext.Response.Headers[HeaderNames.RetryAfter].ToString());

            httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
            using (var reader = new StreamReader(httpContext.Response.Body))
            {
                Assert.AreEqual($"Retry after {exception.RetryAfterHeaderValue} seconds", reader.ReadToEnd());
            }
        }

        class AlwaysExceededMethod : RateLimitMethod
        {
            public readonly int RetryAfterSeconds = DateTimeOffset.UtcNow.Second;

            public override Task DecrementAsync(string limitKey, long cost, ICounterStore store)
            {
                return Task.CompletedTask;
            }

            public override async Task<int> IsExceededAsync(string limitKey, long cost, ICounterStore store)
            {
                return this.RetryAfterSeconds;
            }

            public override Task<bool> IsStillExceededAsync(string limitKey, ICounterStore store)
            {
                throw new NotImplementedException();
            }

            public override string GetCacheKey()
            {
                return string.Empty;
            }
        }

        [TestMethod]
        public async Task InvokeAsyncTest_LimitExceeded_ReturnsRetryAfterResponse()
        {
            // Arrange 

            var counterStoreMock = new Mock<ICounterStore>();

            var limitMethod = new AlwaysExceededMethod();

            var config = new ThrottlingTrollConfig
            {
                Rules = new[]
                {
                    new ThrottlingTrollRule
                    {
                        LimitMethod = limitMethod
                    }
                }
            };

            var options = new ThrottlingTrollOptions
            {
                CounterStore = counterStoreMock.Object,
                GetConfigFunc = () => Task.FromResult(config),
            };

            var middleware = new ThrottlingTrollMiddleware(options);

            var functionContext = new FakeFunctionContext();
            var httpContext = functionContext.GetHttpContext()!;
            httpContext.Response.Body = new MemoryStream();

            // Act

            await middleware.Invoke(functionContext, async () =>
            {
                Assert.Fail("_next() should not be called");

            });

            // Assert

            Assert.AreEqual(StatusCodes.Status429TooManyRequests, httpContext.Response.StatusCode);
            Assert.AreEqual(limitMethod.RetryAfterSeconds.ToString(), httpContext.Response.Headers[HeaderNames.RetryAfter].ToString());

            httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
            using (var reader = new StreamReader(httpContext.Response.Body))
            {
                Assert.AreEqual($"Retry after {limitMethod.RetryAfterSeconds} seconds", reader.ReadToEnd());
            }
        }

        [TestMethod]
        public async Task InvokeAsyncTest_ResponseFabric_ReturnsRetryAfterResponse()
        {
            // Arrange 

            var counterStoreMock = new Mock<ICounterStore>();

            var limitMethod = new AlwaysExceededMethod();

            var config = new ThrottlingTrollConfig
            {
                Rules = new[]
                {
                    new ThrottlingTrollRule
                    {
                        LimitMethod = limitMethod
                    }
                }
            };

            string responseBody = $"my-custom-response-body-{DateTimeOffset.UtcNow}";

            var options = new ThrottlingTrollOptions
            {
                CounterStore = counterStoreMock.Object,
                GetConfigFunc = () => Task.FromResult(config),

                ResponseFabric = async (limitExceededResult, requestProxy, responseProxy, requestAborted) =>
                {
                    responseProxy.StatusCode = StatusCodes.Status402PaymentRequired;

                    await responseProxy.WriteAsync(responseBody);
                }
            };

            var middleware = new ThrottlingTrollMiddleware(options);

            var functionContext = new FakeFunctionContext();
            var httpContext = functionContext.GetHttpContext()!;
            httpContext.Response.Body = new MemoryStream();

            // Act

            await middleware.Invoke(functionContext, async () =>
            {
                Assert.Fail("_next() should not be called");

            });

            // Assert

            Assert.AreEqual(StatusCodes.Status402PaymentRequired, httpContext.Response.StatusCode);

            httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
            using (var reader = new StreamReader(httpContext.Response.Body))
            {
                Assert.AreEqual(responseBody, reader.ReadToEnd());
            }
        }

        [TestMethod]
        public async Task InvokeAsyncTest_ShouldContinueAsNormal_ProceedsWithTheRestOfPipeline()
        {
            // Arrange 

            var counterStoreMock = new Mock<ICounterStore>();

            var limitMethod = new AlwaysExceededMethod();

            var config = new ThrottlingTrollConfig
            {
                Rules = new[]
                {
                    new ThrottlingTrollRule
                    {
                        LimitMethod = limitMethod
                    }
                }
            };

            var options = new ThrottlingTrollOptions
            {
                CounterStore = counterStoreMock.Object,
                GetConfigFunc = () => Task.FromResult(config),

                ResponseFabric = async (limitExceededResult, requestProxy, responseProxy, requestAborted) =>
                {
                    ((IngressHttpResponseProxy)responseProxy).ShouldContinueAsNormal = true;
                }
            };

            bool nextWasCalled = false;

            var middleware = new ThrottlingTrollMiddleware(options);

            var httpContext = new DefaultHttpContext();

            // Act

            await middleware.Invoke(new FakeFunctionContext(), async () =>
            {
                Assert.IsFalse(nextWasCalled);
                nextWasCalled = true;

            });

            // Assert

            Assert.AreEqual(StatusCodes.Status200OK, httpContext.Response.StatusCode);
            Assert.IsTrue(nextWasCalled);
        }

        [TestMethod]
        public async Task InvokeAsyncTest_EgressThrowsTooManyRequestsException_ProceedsWithTheRestOfPipeline()
        {
            // Arrange 

            var counterStoreMock = new Mock<ICounterStore>();

            var limitMethod = new AlwaysExceededMethod();

            var config = new ThrottlingTrollConfig();

            var options = new ThrottlingTrollOptions
            {
                CounterStore = counterStoreMock.Object,
                GetConfigFunc = () => Task.FromResult(config),

                ResponseFabric = async (limitExceededResult, requestProxy, responseProxy, requestAborted) =>
                {
                    ((IngressHttpResponseProxy)responseProxy).ShouldContinueAsNormal = true;
                }
            };

            bool nextWasCalled = false;

            var middleware = new ThrottlingTrollMiddleware(options);

            var httpContext = new DefaultHttpContext();

            // Act

            await middleware.Invoke(new FakeFunctionContext(), async () =>
            {
                Assert.IsFalse(nextWasCalled);
                nextWasCalled = true;

                throw new ThrottlingTrollTooManyRequestsException();

            });

            // Assert

            Assert.AreEqual(StatusCodes.Status200OK, httpContext.Response.StatusCode);
            Assert.IsTrue(nextWasCalled);
        }
    }
}