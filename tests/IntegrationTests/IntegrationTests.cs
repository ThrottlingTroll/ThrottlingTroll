using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using ThrottlingTroll;

namespace IntegrationTests
{
    [TestClass]
    public class IntegrationTests
    {
        const string LocalHost = "http://localhost:17346/";
        static WebApplication? WebApp;

        static HttpClient Client = new HttpClient { BaseAddress = new Uri(LocalHost) };

        static ThrottlingTrollRule NamedCriticalSectionRule = new ThrottlingTrollRule
        {
            Method = "GET",

            UriString = "named-critical-section",

            LimitMethod = new SemaphoreRateLimitMethod
            {
                PermitLimit = 1
            },

            MaxDelayInSeconds = 120,

            IdentityIdExtractor = request =>
            {
                return ((IIncomingHttpRequestProxy)request).Request.Query["id"];
            }
        };

        static ThrottlingTrollRule SemaphoreRule = new ThrottlingTrollRule
        {
            Method = "POST,GET",

            UriString = "semaphore-2-concurrent-requests",

            LimitMethod = new SemaphoreRateLimitMethod
            {
                PermitLimit = 2
            },

            IdentityIdExtractor = request =>
            {
                return ((IIncomingHttpRequestProxy)request).Request.Query["id"];
            }
        };

        static ThrottlingTrollRule DelayedFixedWindowRule = new ThrottlingTrollRule
        {
            Method = "post, get, delete",

            UriString = "delayed-fixed-window",

            LimitMethod = new FixedWindowRateLimitMethod
            {
                PermitLimit = 1,
                IntervalInSeconds = 2
            },

            MaxDelayInSeconds = 120,

            IdentityIdExtractor = request =>
            {
                return ((IIncomingHttpRequestProxy)request).Request.Query["id"];
            }
        };

        static ThrottlingTrollRule FixedWindowFailingMethodRule = new ThrottlingTrollRule
        {
            UriString = "fixed-window-failing-method",

            LimitMethod = new FixedWindowRateLimitMethod
            {
                PermitLimit = 2,
                IntervalInSeconds = 1
            },

            IdentityIdExtractor = request =>
            {
                return ((IIncomingHttpRequestProxy)request).Request.Query["id"];
            }
        };

        class MalfunctioningSemaphoreRateLimitMethod : SemaphoreRateLimitMethod
        {
            public override Task DecrementAsync(string limitKey, long cost, ICounterStore store, IHttpRequestProxy request)
            {
                // Emulating endpoint that crashes in the middle and forgets to decrement the counter
                return Task.CompletedTask;
            }
        }

        static ThrottlingTrollRule SemaphoreCrashingMethodRule = new ThrottlingTrollRule
        {
            UriString = "semaphore-crashing-method",

            LimitMethod = new MalfunctioningSemaphoreRateLimitMethod
            {
                PermitLimit = 1,
                TimeoutInSeconds = 3,
            },

            MaxDelayInSeconds = 120,

            IdentityIdExtractor = request =>
            {
                return ((IIncomingHttpRequestProxy)request).Request.Query["id"];
            },
        };

        static ThrottlingTrollRule SlidingWindowRule = new ThrottlingTrollRule
        {
            UriString = "sliding-window",

            LimitMethod = new SlidingWindowRateLimitMethod
            {
                PermitLimit = 1,
                IntervalInSeconds = 3,
                NumOfBuckets = 3
            },

            IdentityIdExtractor = request =>
            {
                return ((IIncomingHttpRequestProxy)request).Request.Query["id"];
            }
        };

        class ThrowingSemaphoreRateLimitMethod : SemaphoreRateLimitMethod
        {
            public override Task<int> IsExceededAsync(string limitKey, long cost, ICounterStore store, IHttpRequestProxy request)
            {
                throw new Exception("Temporary glitch in Redis");
            }
        }

        static ThrottlingTrollRule ThrowingSemaphoreRule = new ThrottlingTrollRule
        {
            UriString = "throwing-semaphore",

            LimitMethod = new ThrowingSemaphoreRateLimitMethod
            {
                PermitLimit = 1
            },

            MaxDelayInSeconds = 120,

            IdentityIdExtractor = request =>
            {
                return ((IIncomingHttpRequestProxy)request).Request.Query["id"];
            },
        };

        class ThrowingFixedWindowRateLimitMethod : FixedWindowRateLimitMethod
        {
            public override Task<int> IsExceededAsync(string limitKey, long cost, ICounterStore store, IHttpRequestProxy request)
            {
                throw new Exception("Temporary glitch in Redis");
            }
        }

        static ThrottlingTrollRule ThrowingFixedWindowRule = new ThrottlingTrollRule
        {
            UriString = "throwing-fixed-window",

            LimitMethod = new ThrowingFixedWindowRateLimitMethod
            {
                PermitLimit = 10000,
                IntervalInSeconds = 1
            },

            IdentityIdExtractor = request =>
            {
                return ((IIncomingHttpRequestProxy)request).Request.Query["id"];
            }
        };

        static ThrottlingTrollRule DistributedCounterRule = new ThrottlingTrollRule
        {
            UriString = "/distributed-counter",

            LimitMethod = new SemaphoreRateLimitMethod
            {
                PermitLimit = 1
            },

            // This must be set to something > 0 for responses to be automatically delayed
            MaxDelayInSeconds = 120,

            IdentityIdExtractor = request =>
            {
                // Identifying counters by their id
                return ((IIncomingHttpRequestProxy)request).Request.Query["id"];
            }
        };

        static ThrottlingTrollRule DeduplicatingSemaphoreRule = new ThrottlingTrollRule
        {
            UriString = "deduplicating-semaphore",

            LimitMethod = new SemaphoreRateLimitMethod
            {
                PermitLimit = 1,
                ReleaseAfterSeconds = 3
            },

            IdentityIdExtractor = request =>
            {
                return ((IIncomingHttpRequestProxy)request).Request.Query["id"];
            }
        };

        static ThrottlingTrollRule CircuitBreakerRule = new ThrottlingTrollRule
        {
            UriString = "circuit-breaker",

            LimitMethod = new CircuitBreakerRateLimitMethod
            {
                PermitLimit = 3,
                IntervalInSeconds = 1,
                TrialIntervalInSeconds = 2
            },

            IdentityIdExtractor = request =>
            {
                // Identifying counters by their id
                return ((IIncomingHttpRequestProxy)request).Request.Query["id"];
            }
        };

        static ThrottlingTrollRule LeakyBucketRule = new ThrottlingTrollRule
        {
            UriString = "leaky-bucket",

            LimitMethod = new LeakyBucketRateLimitMethod
            {
                PermitLimit = 4,
                IntervalInSeconds = 0.8
            },

            IdentityIdExtractor = request =>
            {
                return ((IIncomingHttpRequestProxy)request).Request.Query["id"];
            }
        };

        static ConcurrentDictionary<string, string> DedupTestValues = new ConcurrentDictionary<string, string>();

        static ConcurrentDictionary<string, bool> CircuitBreakerFailingRequestIds = new ConcurrentDictionary<string, bool>();

        [ClassInitialize]
        public static void Init(TestContext context)
        {
            WebApp = WebApplication.Create();
            WebApp.Urls.Add(LocalHost);

            WebApp.UseThrottlingTroll(options =>
            {
                options.Config = new ThrottlingTrollConfig
                {
                    Rules =
                    [
                        NamedCriticalSectionRule,
                        SemaphoreRule,
                        DelayedFixedWindowRule,
                        FixedWindowFailingMethodRule,
                        SemaphoreCrashingMethodRule,
                        SlidingWindowRule,
                        ThrowingSemaphoreRule,
                        ThrowingFixedWindowRule,
                        DistributedCounterRule,
                        DeduplicatingSemaphoreRule,
                        CircuitBreakerRule,
                        LeakyBucketRule
                    ]
                };
            });

            WebApp.MapGet(NamedCriticalSectionRule.UriString, async () =>
            {
                await Task.Delay(3000);

                return Results.StatusCode((int)HttpStatusCode.OK);
            });

            WebApp.MapGet(SemaphoreRule.UriString, async () =>
            {
                await Task.Delay(1000);

                return Results.StatusCode((int)HttpStatusCode.OK);
            });

            WebApp.MapGet(DelayedFixedWindowRule.UriString, () =>
            {
                return Results.StatusCode((int)HttpStatusCode.OK);
            });

            WebApp.MapGet(FixedWindowFailingMethodRule.UriString, () =>
            {
                throw new Exception();
            });

            WebApp.MapGet(SemaphoreCrashingMethodRule.UriString, () =>
            {
                return Results.StatusCode((int)HttpStatusCode.OK);
            });

            WebApp.MapGet(SlidingWindowRule.UriString, () =>
            {
                return Results.StatusCode((int)HttpStatusCode.OK);
            });

            WebApp.MapGet(ThrowingSemaphoreRule.UriString, () =>
            {
                // Should never be called
                return Results.StatusCode((int)HttpStatusCode.MethodNotAllowed);
            });

            WebApp.MapGet(ThrowingFixedWindowRule.UriString, () =>
            {
                return Results.StatusCode((int)HttpStatusCode.Accepted);
            });

            var counters = new ConcurrentDictionary<string, long>();

            WebApp.MapGet(DistributedCounterRule.UriString, ([FromQuery] string id) =>
            {
                // The below code is intentionally not thread-safe

                long counter = 1;
                string counterId = $"TestCounter{id}";

                if (counters.ContainsKey(counterId))
                {
                    counter = counters[counterId];

                    counter++;
                }

                Thread.Sleep(Random.Shared.Next(100));

                counters[counterId] = counter;

                return counter;
            });

            WebApp.MapGet(DeduplicatingSemaphoreRule.UriString, ([FromQuery] string id, [FromQuery] string val) =>
            {
                DedupTestValues[id] = val;
            });

            WebApp.MapGet(CircuitBreakerRule.UriString, ([FromQuery] string id) =>
            {
                bool isBadRequest = CircuitBreakerFailingRequestIds.ContainsKey(id);

                return Results.StatusCode(isBadRequest ? (int)HttpStatusCode.BadRequest : (int)HttpStatusCode.OK);
            });

            WebApp.MapGet(LeakyBucketRule.UriString, () =>
            {
                return Results.StatusCode((int)HttpStatusCode.OK);
            });

            WebApp.Start();
        }

        [ClassCleanup]
        public static void TearDown()
        {
            WebApp.DisposeAsync();
        }

        [TestMethod]
        public async Task TestCounters()
        {
            await Task.WhenAll(
                Enumerable.Range(0, 64)
                    .Select(_ => this.TestCounter())
            );
        }

        [TestMethod]
        public async Task TestNamedCriticalSections1()
        {
            await Task.WhenAll(
                Enumerable.Range(0, 32)
                    .Select(_ => this.TestNamedCriticalSection1())
            );
        }

        [TestMethod]
        public async Task TestNamedCriticalSections2()
        {
            await Task.WhenAll(
                Enumerable.Range(0, 16)
                    .Select(_ => this.TestNamedCriticalSection2())
            );
        }

        [TestMethod]
        public async Task TestSemaphores()
        {
            await Task.WhenAll(
                Enumerable.Range(0, 16)
                    .Select(async _ => {

                        for (int i = 0; i < 10; i++)
                        {
                            await this.TestSemaphore();
                        }
                    })
            );
        }

        [TestMethod]
        public async Task TestDelayedFixedWindows()
        {
            await Task.WhenAll(
                Enumerable.Range(0, 8)
                    .Select(_ => this.TestDelayedFixedWindow())
            );
        }

        [TestMethod]
        public async Task TestFixedWindowFailingMethods()
        {
            await Task.WhenAll(
                Enumerable.Range(0, 16)
                    .Select(_ => this.TestFixedWindowFailingMethod())
            );
        }

        [TestMethod]
        public async Task TestSemaphoreCrashingMethods()
        {
            await Task.WhenAll(
                Enumerable.Range(0, 16)
                    .Select(_ => this.TestSemaphoreCrashingMethod())
            );
        }

        [TestMethod]
        public async Task TestSlidingWindows()
        {
            await Task.WhenAll(
                Enumerable.Range(0, 16)
                    .Select(_ => this.TestSlidingWindow())
            );
        }

        [TestMethod]
        public async Task TestThrowingSemaphores()
        {
            await Task.WhenAll(
                Enumerable.Range(0, 16)
                    .Select(_ => this.TestThrowingSemaphore())
            );
        }

        [TestMethod]
        public async Task TestThrowingFixedWindows()
        {
            await Task.WhenAll(
                Enumerable.Range(0, 16)
                    .Select(_ => this.TestThrowingFixedWindow())
            );
        }

        [TestMethod]
        public async Task TestDeduplications()
        {
            await Task.WhenAll(
                Enumerable.Range(0, 16)
                    .Select(_ => this.TestDeduplication())
            );
        }

        [TestMethod]
        public async Task TestCircuitBreakers()
        {
            await Task.WhenAll(
                Enumerable.Range(0, 16)
                    .Select(_ => this.TestCircuitBreaker())
            );
        }

        [TestMethod]
        public async Task TestLeakyBuckets()
        {
            await Task.WhenAll(
                Enumerable.Range(0, 8)
                    .Select(_ => this.TestLeakyBucket())
            );
        }


        private async Task TestNamedCriticalSection1()
        {
            Guid id = Guid.NewGuid();

            // First call
            var sw1 = new Stopwatch(); sw1.Start();
            var task1 = Client.GetAsync($"{NamedCriticalSectionRule.UriString}?id={id}").ContinueWith(_ => sw1.Stop());

            // The test endpoint sleeps for 3 seconds. Waiting for 2 seconds before making the next call.
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Second call
            var sw2 = new Stopwatch(); sw2.Start();
            var task2 = Client.GetAsync($"{NamedCriticalSectionRule.UriString}?id={id}").ContinueWith(_ => sw2.Stop());

            await Task.WhenAll(task1,task2);

            // Third call
            var sw3 = new Stopwatch(); sw3.Start();
            await Client.GetAsync($"{NamedCriticalSectionRule.UriString}?id={id}");
            sw3.Stop();

            string msg = $"First: {sw1.ElapsedMilliseconds} ms, second: {sw2.ElapsedMilliseconds} ms, third: {sw3.ElapsedMilliseconds} ms";

            Assert.IsTrue(sw1.ElapsedMilliseconds >= 2990, msg);
            Assert.IsTrue(sw1.ElapsedMilliseconds < 3600, msg);

            Assert.IsTrue(sw2.ElapsedMilliseconds >= 3990, msg);
            Assert.IsTrue(sw2.ElapsedMilliseconds < 4600, msg);

            Assert.IsTrue(sw3.ElapsedMilliseconds >= 2990, msg);
            Assert.IsTrue(sw3.ElapsedMilliseconds < 3600, msg);
        }

        private async Task TestNamedCriticalSection2()
        {
            Guid id = Guid.NewGuid();

            var tasks = Enumerable.Range(0, 5).Select(_ => {

                var sw = new Stopwatch(); 
                sw.Start();

                return Client
                    .GetAsync($"{NamedCriticalSectionRule.UriString}?id={id}")
                    .ContinueWith(_ =>
                    {
                        sw.Stop();
                        return sw.ElapsedMilliseconds;
                    });
            });

            var times = await Task.WhenAll(tasks);

            string msg = "Milliseconds: " + string.Join(", ", times);
            Trace.WriteLine(msg);

            Assert.IsTrue(times.SingleOrDefault(t => t >= 2990 && t < 3200) > 0, msg);
            Assert.IsTrue(times.SingleOrDefault(t => t >= 5990 && t < 6300) > 0, msg);
            Assert.IsTrue(times.SingleOrDefault(t => t >= 8990 && t < 9400) > 0, msg);
            Assert.IsTrue(times.SingleOrDefault(t => t >= 11990 && t < 12500) > 0, msg);
            Assert.IsTrue(times.SingleOrDefault(t => t >= 14990 && t < 15600) > 0, msg);
        }

        private async Task TestSemaphore()
        {
            Guid id = Guid.NewGuid();

            var successfulCallTasks = Enumerable.Range(0, 2).Select(_ => Client.GetAsync($"{SemaphoreRule.UriString}?id={id}")).ToArray();

            await Task.Delay(TimeSpan.FromMilliseconds(300));

            var throttledCallTasks = Enumerable.Range(0, 5).Select(_ => Client.GetAsync($"{SemaphoreRule.UriString}?id={id}")).ToArray();

            var successfulCalls = await Task.WhenAll(successfulCallTasks);

            var throttledCalls = await Task.WhenAll(throttledCallTasks);

            Trace.WriteLine($"Successful: {string.Join(",", successfulCalls.Select(c => c.StatusCode))}");
            Trace.WriteLine($"Throttled: {string.Join(",", throttledCalls.Select(c => c.StatusCode))}");

            foreach (var r in successfulCalls)
            {
                Assert.AreEqual(HttpStatusCode.OK, r.StatusCode);
            }

            foreach (var r in throttledCalls)
            {
                Assert.AreEqual(HttpStatusCode.TooManyRequests, r.StatusCode);
            }
        }

        private async Task TestDelayedFixedWindow()
        {
            Guid id = Guid.NewGuid();

            var tasks = Enumerable.Range(0, 5).Select(_ => {

                var sw = new Stopwatch();
                sw.Start();

                return Client
                    .GetAsync($"{DelayedFixedWindowRule.UriString}?id={id}")
                    .ContinueWith(t =>
                    {
                        Assert.AreEqual(HttpStatusCode.OK, t.Result.StatusCode);

                        sw.Stop();
                        return sw.ElapsedMilliseconds;
                    });
            });

            var times = await Task.WhenAll(tasks);

            string msg = "Milliseconds: " + string.Join(", ", times);
            Trace.WriteLine(msg);

            Assert.IsTrue(times.Single(t => t >= 0 && t < 500) >= 0, msg);
            Assert.IsTrue(times.Single(t => t >= 1000 && t < 2500) > 0, msg);
            Assert.IsTrue(times.Single(t => t >= 3000 && t < 4500) > 0, msg);
            Assert.IsTrue(times.Single(t => t >= 5000 && t < 6500) > 0, msg);
            Assert.IsTrue(times.Single(t => t >= 7000 && t < 8500) > 0, msg);
        }

        private async Task TestFixedWindowFailingMethod()
        {
            Guid id = Guid.NewGuid();

            for (int i = 0; i < 3; i++)
            {
                var tasks = Enumerable.Range(0, 5).Select(_ => {

                    return Client
                        .GetAsync($"{FixedWindowFailingMethodRule.UriString}?id={id}")
                        .ContinueWith(t => t.Result.StatusCode);
                });

                var statusCodes = await Task.WhenAll(tasks);

                string msg = "Results: " + string.Join(", ", statusCodes);
                Trace.WriteLine(msg);

                Assert.AreEqual(2, statusCodes.Count(c => c == HttpStatusCode.InternalServerError), msg);
                Assert.AreEqual(3, statusCodes.Count(c => c == HttpStatusCode.TooManyRequests), msg);

                await Task.Delay(1000);
            }
        }

        private async Task TestSemaphoreCrashingMethod()
        {
            Guid id = Guid.NewGuid();

            // First call
            var sw1 = new Stopwatch(); sw1.Start();
            var task1 = Client.GetAsync($"{SemaphoreCrashingMethodRule.UriString}?id={id}").ContinueWith(t => { sw1.Stop(); return t.Result.StatusCode; });

            await Task.Delay(TimeSpan.FromMilliseconds(100));

            // Second call
            var sw2 = new Stopwatch(); sw2.Start();
            var task2 = Client.GetAsync($"{SemaphoreCrashingMethodRule.UriString}?id={id}").ContinueWith(t => { sw2.Stop(); return t.Result.StatusCode; });

            await Task.WhenAll(task1, task2);

            string msg = $"First: {sw1.ElapsedMilliseconds} ms, second: {sw2.ElapsedMilliseconds} ms";

            Trace.WriteLine(msg);

            Assert.IsTrue(sw1.ElapsedMilliseconds < 300, msg);
            Assert.IsTrue(sw2.ElapsedMilliseconds >= 2500, msg);
            Assert.IsTrue(sw2.ElapsedMilliseconds < 3500, msg);

            Assert.AreEqual(HttpStatusCode.OK, task1.Result);
            Assert.AreEqual(HttpStatusCode.OK, task2.Result);
        }

        private async Task TestSlidingWindow()
        {
            Guid id = Guid.NewGuid();

            // First call - should succeed
            var result = await Client.GetAsync($"{SlidingWindowRule.UriString}?id={id}");

            Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);

            // Now keep trying for 6 seconds
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(300);

                result = await Client.GetAsync($"{SlidingWindowRule.UriString}?id={id}");

                Assert.AreEqual(HttpStatusCode.TooManyRequests, result.StatusCode);
            }

            // Now waiting till the window expires entirely
            await Task.Delay(4000);

            // Should succeed again
            result = await Client.GetAsync($"{SlidingWindowRule.UriString}?id={id}");

            Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
        }

        private async Task TestThrowingSemaphore()
        {
            Guid id = Guid.NewGuid();

            // Now keep trying for 2 seconds
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(100);

                var result = await Client.GetAsync($"{ThrowingSemaphoreRule.UriString}?id={id}");

                Assert.AreEqual(HttpStatusCode.InternalServerError, result.StatusCode);
            }
        }

        private async Task TestThrowingFixedWindow()
        {
            Guid id = Guid.NewGuid();

            // Now keep trying for 2 seconds
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(100);

                var result = await Client.GetAsync($"{ThrowingFixedWindowRule.UriString}?id={id}");

                Assert.AreEqual(HttpStatusCode.Accepted, result.StatusCode);
            }
        }

        private async Task TestCounter()
        {
            Guid id = Guid.NewGuid();

            var bag = new ConcurrentBag<long>();

            int attempts = 64;

            await Task.WhenAll(
                Enumerable.Range(0, attempts)
                    .Select(async _ =>
                    {
                        var response = await Client.GetAsync($"{DistributedCounterRule.UriString}?id={id}");

                        long counter = long.Parse(await response.Content.ReadAsStringAsync());

                        bag.Add(counter);
                    })
            );

            long diff = bag.Max() - bag.Min();

            Assert.AreEqual(attempts - 1, diff, id.ToString());
        }

        private async Task TestDeduplication()
        {
            string requestId = Guid.NewGuid().ToString();
            string winningRequestValue = "";
            int throttledRequests = 0;
            int totalRequests = 0;

            int attempts = 64;

            await Task.WhenAll(
                Enumerable.Range(0, attempts)
                    .Select(async _ =>
                    {
                        await Task.Delay(Random.Shared.Next(0, 500));

                        string requestValue = Guid.NewGuid().ToString();

                        var response = await Client.GetAsync($"{DeduplicatingSemaphoreRule.UriString}?id={requestId}&val={requestValue}");

                        if (response.IsSuccessStatusCode)
                        {
                            winningRequestValue = requestValue;
                        }
                        else
                        {
                            Interlocked.Increment(ref throttledRequests);
                        }

                        Interlocked.Increment(ref totalRequests);
                    })
            );

            Assert.AreEqual(attempts, totalRequests);

            Assert.AreEqual(attempts - 1, throttledRequests);

            Assert.AreEqual(DedupTestValues[requestId], winningRequestValue);
        }

        private async Task TestCircuitBreaker()
        {
            Guid id = Guid.NewGuid();

            // Making three failing requests

            var response = await Client.GetAsync($"{CircuitBreakerRule.UriString}?id={id}");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            CircuitBreakerFailingRequestIds[id.ToString()] = true;
            response = await Client.GetAsync($"{CircuitBreakerRule.UriString}?id={id}");
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            CircuitBreakerFailingRequestIds.TryRemove(id.ToString(), out bool _);
            response = await Client.GetAsync($"{CircuitBreakerRule.UriString}?id={id}");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            CircuitBreakerFailingRequestIds[id.ToString()] = true;
            response = await Client.GetAsync($"{CircuitBreakerRule.UriString}?id={id}");
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            CircuitBreakerFailingRequestIds.TryRemove(id.ToString(), out bool _);
            response = await Client.GetAsync($"{CircuitBreakerRule.UriString}?id={id}");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            CircuitBreakerFailingRequestIds[id.ToString()] = true;
            response = await Client.GetAsync($"{CircuitBreakerRule.UriString}?id={id}");
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            CircuitBreakerFailingRequestIds.TryRemove(id.ToString(), out bool _);
            response = await Client.GetAsync($"{CircuitBreakerRule.UriString}?id={id}");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            // This fourth failing request will turn the rule into Trial mode (yet the client will still get BadRequest, because that switch happens post factum)

            CircuitBreakerFailingRequestIds[id.ToString()] = true;
            response = await Client.GetAsync($"{CircuitBreakerRule.UriString}?id={id}");
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);

            // From now on the rule should be in Trial mode - testing it for 5 seconds

            int badRequestCount = 0;
            int serviceUnavailableCount = 0;
            for (int i = 0; i < 50; i++)
            {
                response = await Client.GetAsync($"{CircuitBreakerRule.UriString}?id={id}");

                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    badRequestCount++;
                }
                else if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    serviceUnavailableCount++;
                }

                await Task.Delay(100);
            }

            Assert.IsTrue(badRequestCount > 2 && badRequestCount < 7, $"badRequestCount = {badRequestCount}");
            Assert.AreEqual(50, badRequestCount + serviceUnavailableCount);

            // Need to wait till the trial interval ends
            await Task.Delay(2000);

            // Making a successful request
            CircuitBreakerFailingRequestIds.TryRemove(id.ToString(), out bool _);
            response = await Client.GetAsync($"{CircuitBreakerRule.UriString}?id={id}");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            // Now it should be back to normal
            for (int i = 0; i < 5; i++)
            {
                response = await Client.GetAsync($"{CircuitBreakerRule.UriString}?id={id}");
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            }
        }

        private async Task TestLeakyBucket()
        {
            // Warm-up
            await Client.GetAsync($"{LeakyBucketRule.UriString}?id={Guid.NewGuid()}");

            Guid id = Guid.NewGuid();

            for (int i = 0; i < 3; i++)
            {
                var tasks = Enumerable.Range(0, 6).Select(_ => {

                    var sw = new Stopwatch();
                    sw.Start();

                    return Client
                        .GetAsync($"{LeakyBucketRule.UriString}?id={id}")
                        .ContinueWith(t => (t.Result.StatusCode, sw.ElapsedMilliseconds));
                });

                var results = await Task.WhenAll(tasks);

                string msg = string.Join(
                    " | ",
                    results
                        .OrderBy(r => r.ElapsedMilliseconds)
                        .Select(r => $"{r.StatusCode}({r.ElapsedMilliseconds}ms)"));

                Trace.WriteLine("Results: " + msg);

                Assert.AreEqual(4, results.Count(r => r.StatusCode == HttpStatusCode.OK), msg);
                Assert.AreEqual(2, results.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests), msg);

                var lats = results
                    .Where(r => r.StatusCode == HttpStatusCode.OK)
                    .OrderBy(r => r.ElapsedMilliseconds)
                    .Select(r => r.ElapsedMilliseconds)
                    .ToArray();

                Assert.AreEqual(4, lats.Length);

                Assert.IsTrue(lats[0] >=   0 && lats[0] <=  50, $"0: {lats[0]}ms");
                Assert.IsTrue(lats[1] >= 190 && lats[1] <= 250, $"1: {lats[1]}ms");
                Assert.IsTrue(lats[2] >= 390 && lats[2] <= 450, $"2: {lats[2]}ms");
                Assert.IsTrue(lats[3] >= 590 && lats[3] <= 650, $"3: {lats[3]}ms");

                await Task.Delay(300);
            }
        }
    }
}