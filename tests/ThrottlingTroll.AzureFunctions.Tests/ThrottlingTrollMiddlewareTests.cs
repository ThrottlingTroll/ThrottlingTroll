using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Moq;
using System.Net;
using System.Security.Claims;

namespace ThrottlingTroll.AzureFunctions.Tests
{
    class FakeHttpResponseData : HttpResponseData
    {
        public FakeHttpResponseData(FunctionContext functionContext) : base(functionContext)
        {
            this.Headers = new HttpHeadersCollection();
            this.Body = new MemoryStream();
        }

        public override HttpStatusCode StatusCode { get; set; }
        public override HttpHeadersCollection Headers { get; set; }
        public override Stream Body { get; set; }

        public override HttpCookies Cookies { get; }
    }

    class FakeHttpRequestData : HttpRequestData
    {
        public FakeHttpRequestData() : base(new FakeFunctionContext())
        {
        }

        public override Stream Body => throw new NotImplementedException();

        public override HttpHeadersCollection Headers { get; }

        public override IReadOnlyCollection<IHttpCookie> Cookies => throw new NotImplementedException();

        public override Uri Url { get; }

        public override IEnumerable<ClaimsIdentity> Identities => throw new NotImplementedException();

        public override string Method { get; }

        public override HttpResponseData CreateResponse()
        {
            return new FakeHttpResponseData(this.FunctionContext);
        }
    }

    class FakeFunctionContext : FunctionContext
    {
        public override FunctionDefinition FunctionDefinition => throw new NotImplementedException();

        public override IServiceProvider InstanceServices { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public override string InvocationId => throw new NotImplementedException();

        public override string FunctionId => throw new NotImplementedException();

        public override TraceContext TraceContext => throw new NotImplementedException();

        public override BindingContext BindingContext => throw new NotImplementedException();

        public override RetryContext RetryContext => throw new NotImplementedException();

        public override IDictionary<object, object> Items { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public override IInvocationFeatures Features => throw new NotImplementedException();
    }

    class AlwaysExceededMethod : RateLimitMethod
    {
        public readonly int RetryAfterSeconds = DateTimeOffset.UtcNow.Second;

        public override Task DecrementAsync(string limitKey, ICounterStore store)
        {
            return Task.CompletedTask;
        }

        public override async Task<int> IsExceededAsync(string limitKey, ICounterStore store)
        {
            return this.RetryAfterSeconds;
        }

        public override Task<bool> IsStillExceededAsync(string limitKey, ICounterStore store)
        {
            throw new NotImplementedException();
        }
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

            var result = await middleware.Invoke(new FakeHttpRequestData(), async () =>
            {
                Assert.IsFalse(nextWasCalled);

                nextWasCalled = true;

            }, default);

            // Assert

            Assert.IsNull(result);
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

            Func<Task<HttpResponseData>> act = () => middleware.Invoke(new FakeHttpRequestData(), async () =>
            {
                throw exception;

            }, default);

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
                RetryAfterHeaderValue = DateTimeOffset.UtcNow.ToString("ddd, dd MMM yyy HH:mm:ss 'GMT'")
            };

            var middleware = new ThrottlingTrollMiddleware(options);

            // Act

            var result = await middleware.Invoke(new FakeHttpRequestData(), async () =>
            {
                throw exception;

            }, default);

            // Assert

            Assert.AreEqual(HttpStatusCode.TooManyRequests, result.StatusCode);
            Assert.AreEqual(exception.RetryAfterHeaderValue, result.Headers.GetValues("Retry-After").Single());

            result.Body.Seek(0, SeekOrigin.Begin);
            using (var reader = new StreamReader(result.Body))
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
                RetryAfterHeaderValue = DateTimeOffset.UtcNow.ToString("ddd, dd MMM yyy HH:mm:ss 'GMT'")
            };

            var middleware = new ThrottlingTrollMiddleware(options);

            // Act

            var result = await middleware.Invoke(new FakeHttpRequestData(), async () =>
            {
                Task.Run(() => { throw exception; }).Wait();

            }, default);

            // Assert

            Assert.AreEqual(HttpStatusCode.TooManyRequests, result.StatusCode);
            Assert.AreEqual(exception.RetryAfterHeaderValue, result.Headers.GetValues("Retry-After").Single());

            result.Body.Seek(0, SeekOrigin.Begin);
            using (var reader = new StreamReader(result.Body))
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

            // Act

            var result = await middleware.Invoke(new FakeHttpRequestData(), async () =>
            {
                throw exception;

            }, default);

            // Assert

            Assert.AreEqual(HttpStatusCode.TooManyRequests, result.StatusCode);
            Assert.AreEqual(exception.RetryAfterHeaderValue, result.Headers.GetValues("Retry-After").Single());

            result.Body.Seek(0, SeekOrigin.Begin);
            using (var reader = new StreamReader(result.Body))
            {
                Assert.AreEqual($"Retry after {exception.RetryAfterHeaderValue} seconds", reader.ReadToEnd());
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

            // Act

            var result = await middleware.Invoke(new FakeHttpRequestData(), async () =>
            {
                Assert.Fail("_next() should not be called");

            }, default);

            // Assert

            Assert.AreEqual(HttpStatusCode.TooManyRequests, result.StatusCode);
            Assert.AreEqual(limitMethod.RetryAfterSeconds.ToString(), result.Headers.GetValues("Retry-After").Single());

            result.Body.Seek(0, SeekOrigin.Begin);
            using (var reader = new StreamReader(result.Body))
            {
                Assert.AreEqual($"Retry after {limitMethod.RetryAfterSeconds.ToString()} seconds", reader.ReadToEnd());
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
                    responseProxy.StatusCode = (int)HttpStatusCode.PaymentRequired;

                    await responseProxy.WriteAsync(responseBody);
                }
            };

            var middleware = new ThrottlingTrollMiddleware(options);

            // Act

            var result = await middleware.Invoke(new FakeHttpRequestData(), async () =>
            {
                Assert.Fail("_next() should not be called");

            }, default);

            // Assert

            Assert.AreEqual(HttpStatusCode.PaymentRequired, result.StatusCode);

            result.Body.Seek(0, SeekOrigin.Begin);
            using (var reader = new StreamReader(result.Body))
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

            // Act

            var result = await middleware.Invoke(new FakeHttpRequestData(), async () =>
            {
                Assert.IsFalse(nextWasCalled);
                nextWasCalled = true;

            }, default);

            // Assert

            Assert.IsNull(result);
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

            // Act

            var result = await middleware.Invoke(new FakeHttpRequestData(), async () =>
            {
                Assert.IsFalse(nextWasCalled);
                nextWasCalled = true;

            }, default);

            // Assert

            Assert.IsNull(result);
            Assert.IsTrue(nextWasCalled);
        }
    }
}