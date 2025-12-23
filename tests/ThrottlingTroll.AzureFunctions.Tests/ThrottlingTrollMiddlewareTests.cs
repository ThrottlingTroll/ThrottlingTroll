using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Context.Features;
using Microsoft.Azure.Functions.Worker.Http;
using Moq;
using System.Collections;
using System.Collections.Specialized;
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

        public FakeHttpRequestData(FunctionContext ctx) : base(ctx)
        {
        }

        public override Stream Body => throw new NotImplementedException();

        public override HttpHeadersCollection Headers { get; }

        public override NameValueCollection Query {  get; }

        public override IReadOnlyCollection<IHttpCookie> Cookies => throw new NotImplementedException();

        public override Uri Url { get; }

        public override IEnumerable<ClaimsIdentity> Identities => throw new NotImplementedException();

        public override string Method { get; }

        public override HttpResponseData CreateResponse()
        {
            return new FakeHttpResponseData(this.FunctionContext);
        }
    }

    class FakeHttpRequestDataFeature : IHttpRequestDataFeature
    {
        public ValueTask<HttpRequestData?> GetHttpRequestDataAsync(FunctionContext context)
        {
            HttpRequestData data = new FakeHttpRequestData(context);
            return ValueTask.FromResult(data)!;
        }
    }

    class FakeInvocationFeatures : IInvocationFeatures
    {
        public T? Get<T>()
        {
            if (typeof(T) == typeof(IHttpRequestDataFeature))
            {
                return (T)(object)new FakeHttpRequestDataFeature();
            }

            throw FakeFunctionContext.GetHttpResponseDataWasCalledException;
        }

        public IEnumerator<KeyValuePair<Type, object>> GetEnumerator()
        {
            throw new NotImplementedException();
        }

        public void Set<T>(T instance)
        {
            throw new NotImplementedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
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

        public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();


        public static Exception GetHttpResponseDataWasCalledException = new Exception("This indicates that request.FunctionContext.GetHttpResponseData() was called");

        public override IInvocationFeatures Features { get { return new FakeInvocationFeatures(); } }
    }

    class AlwaysExceededMethod : RateLimitMethod
    {
        public override int RetryAfterInSeconds => DateTimeOffset.UtcNow.Second;

        public override Task DecrementAsync(string limitKey, long cost, ICounterStore store, IHttpRequestProxy request)
        {
            return Task.CompletedTask;
        }

        public override async Task<int> IsExceededAsync(string limitKey, long cost, ICounterStore store, IHttpRequestProxy request)
        {
            return -1;
        }

        public override Task<bool> IsStillExceededAsync(string limitKey, ICounterStore store, IHttpRequestProxy request)
        {
            throw new NotImplementedException();
        }
        public override string GetCacheKey()
        {
            return string.Empty;
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

            try
            {
                await middleware.InvokeAsync(new FakeFunctionContext(), async () =>
                {
                    Assert.IsFalse(nextWasCalled);

                    nextWasCalled = true;

                });
            }
            catch (Exception ex)
            {
                // Couldn't find a way to mock FunctionContext.GetHttpResponseData(), so instead just checking
                // that this particular exception instance was thrown by FakeFunctionContext

                Assert.AreEqual(FakeFunctionContext.GetHttpResponseDataWasCalledException, ex);
            }

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

            Func<Task> act = () => middleware.InvokeAsync(new FakeFunctionContext(), async () =>
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

            // Act

            var ctx = new FakeFunctionContext();
            var result = (IngressHttpResponseDataProxy)await middleware.InvokeAsync(ctx, async () =>
            {
                throw exception;

            });

            // Assert

            Assert.AreEqual(HttpStatusCode.TooManyRequests, result.ResponseData.StatusCode);
            Assert.AreEqual(exception.RetryAfterHeaderValue, result.ResponseData.Headers.GetValues("Retry-After").Single());

            result.ResponseData.Body.Seek(0, SeekOrigin.Begin);
            using (var reader = new StreamReader(result.ResponseData.Body))
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

            // Act

            var ctx = new FakeFunctionContext();
            var result = (IngressHttpResponseDataProxy)await middleware.InvokeAsync(ctx, async () =>
            {
                Task.Run(() => { throw exception; }).Wait();

            });

            // Assert

            Assert.AreEqual(HttpStatusCode.TooManyRequests, result.ResponseData.StatusCode);
            Assert.AreEqual(exception.RetryAfterHeaderValue, result.ResponseData.Headers.GetValues("Retry-After").Single());

            result.ResponseData.Body.Seek(0, SeekOrigin.Begin);
            using (var reader = new StreamReader(result.ResponseData.Body))
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

            var ctx = new FakeFunctionContext();
            var result = (IngressHttpResponseDataProxy)await middleware.InvokeAsync(ctx, async () =>
            {
                throw exception;

            });

            // Assert
            Assert.AreEqual(HttpStatusCode.TooManyRequests, result.ResponseData.StatusCode);
            Assert.AreEqual(exception.RetryAfterHeaderValue, result.ResponseData.Headers.GetValues("Retry-After").Single());

            result.ResponseData.Body.Seek(0, SeekOrigin.Begin);
            using (var reader = new StreamReader(result.ResponseData.Body))
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

            var ctx = new FakeFunctionContext();
            var result = (IngressHttpResponseDataProxy)await middleware.InvokeAsync(ctx, async () =>
            {
                Assert.Fail("_next() should not be called");

            });

            // Assert

            Assert.AreEqual(HttpStatusCode.TooManyRequests, result.ResponseData.StatusCode);
            Assert.AreEqual(limitMethod.RetryAfterInSeconds.ToString(), result.ResponseData.Headers.GetValues("Retry-After").Single());

            result.ResponseData.Body.Seek(0, SeekOrigin.Begin);
            using (var reader = new StreamReader(result.ResponseData.Body))
            {
                Assert.AreEqual($"Retry after {limitMethod.RetryAfterInSeconds.ToString()} seconds", reader.ReadToEnd());
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

            var ctx = new FakeFunctionContext();
            var result = (IngressHttpResponseDataProxy)await middleware.InvokeAsync(ctx, async () =>
            {
                Assert.Fail("_next() should not be called");

            });

            // Assert

            Assert.AreEqual(HttpStatusCode.PaymentRequired, result.ResponseData.StatusCode);

            result.ResponseData.Body.Seek(0, SeekOrigin.Begin);
            using (var reader = new StreamReader(result.ResponseData.Body))
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
                    ((IngressHttpResponseDataProxy)responseProxy).ShouldContinueAsNormal = true;
                }
            };

            bool nextWasCalled = false;

            var middleware = new ThrottlingTrollMiddleware(options);

            // Act

            try
            {
                var ctx = new FakeFunctionContext();
                await middleware.InvokeAsync(ctx, async () =>
                {
                    Assert.IsFalse(nextWasCalled);
                    nextWasCalled = true;

                });
            }
            catch (Exception ex)
            {
                // Couldn't find a way to mock FunctionContext.GetHttpResponseData(), so instead just checking
                // that this particular exception instance was thrown by FakeFunctionContext

                Assert.AreEqual(FakeFunctionContext.GetHttpResponseDataWasCalledException, ex);
            }

            // Assert

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
                    ((IngressHttpResponseDataProxy)responseProxy).ShouldContinueAsNormal = true;
                }
            };

            bool nextWasCalled = false;

            var middleware = new ThrottlingTrollMiddleware(options);

            // Act

            try
            {
                await middleware.InvokeAsync(new FakeFunctionContext(), async () =>
                {
                    Assert.IsFalse(nextWasCalled);
                    nextWasCalled = true;

                });
            }
            catch (Exception ex) 
            {
                // Couldn't find a way to mock FunctionContext.GetHttpResponseData(), so instead just checking
                // that this particular exception instance was thrown by FakeFunctionContext

                Assert.AreEqual(FakeFunctionContext.GetHttpResponseDataWasCalledException, ex);
            }

            // Assert

            Assert.IsTrue(nextWasCalled);
        }
    }
}