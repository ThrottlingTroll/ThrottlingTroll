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
            UriPattern = "named-critical-section",

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
            UriPattern = "semaphore-2-concurrent-requests",

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
            UriPattern = "delayed-fixed-window",

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
            UriPattern = "fixed-window-failing-method",

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
            public override Task DecrementAsync(string limitKey, ICounterStore store)
            {
                // Emulating endpoint that crushes in the middle and forgets to decrement the counter
                return Task.CompletedTask;
            }
        }

        static ThrottlingTrollRule SemaphoreCrushingMethodRule = new ThrottlingTrollRule
        {
            UriPattern = "semaphore-crushing-method",

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
            UriPattern = "sliding-window",

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


        [ClassInitialize]
        public static void Init(TestContext context)
        {
            WebApp = WebApplication.Create();
            WebApp.Urls.Add(LocalHost);

            WebApp.UseThrottlingTroll(options =>
            {
                options.Config = new ThrottlingTrollConfig
                {
                    Rules = new[]
                    {
                        NamedCriticalSectionRule,
                        SemaphoreRule,
                        DelayedFixedWindowRule,
                        FixedWindowFailingMethodRule,
                        SemaphoreCrushingMethodRule,
                        SlidingWindowRule
                    }
                };
            });

            WebApp.MapGet(NamedCriticalSectionRule.UriPattern, async () =>
            {
                await Task.Delay(3000);

                return Results.StatusCode((int)HttpStatusCode.OK);
            });

            WebApp.MapGet(SemaphoreRule.UriPattern, async () =>
            {
                await Task.Delay(1000);

                return Results.StatusCode((int)HttpStatusCode.OK);
            });

            WebApp.MapGet(DelayedFixedWindowRule.UriPattern, () =>
            {
                return Results.StatusCode((int)HttpStatusCode.OK);
            });

            WebApp.MapGet(FixedWindowFailingMethodRule.UriPattern, () =>
            {
                throw new Exception();
            });

            WebApp.MapGet(SemaphoreCrushingMethodRule.UriPattern, () =>
            {
                return Results.StatusCode((int)HttpStatusCode.OK);
            });

            WebApp.MapGet(SlidingWindowRule.UriPattern, () =>
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
        public async Task TestSemaphoreCrushingMethods()
        {
            await Task.WhenAll(
                Enumerable.Range(0, 16)
                    .Select(_ => this.TestSemaphoreCrushingMethod())
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

        private async Task TestNamedCriticalSection1()
        {
            Guid id = Guid.NewGuid();

            // First call
            var sw1 = new Stopwatch(); sw1.Start();
            var task1 = Client.GetAsync($"{NamedCriticalSectionRule.UriPattern}?id={id}").ContinueWith(_ => sw1.Stop());

            // The test endpoint sleeps for 3 seconds. Waiting for 2 seconds before making the next call.
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Second call
            var sw2 = new Stopwatch(); sw2.Start();
            var task2 = Client.GetAsync($"{NamedCriticalSectionRule.UriPattern}?id={id}").ContinueWith(_ => sw2.Stop());

            await Task.WhenAll(task1,task2);

            // Third call
            var sw3 = new Stopwatch(); sw3.Start();
            await Client.GetAsync($"{NamedCriticalSectionRule.UriPattern}?id={id}");
            sw3.Stop();

            string msg = $"First: {sw1.ElapsedMilliseconds} ms, second: {sw2.ElapsedMilliseconds} ms, third: {sw3.ElapsedMilliseconds} ms";

            Assert.IsTrue(sw1.ElapsedMilliseconds >= 2998, msg);
            Assert.IsTrue(sw1.ElapsedMilliseconds < 3600, msg);

            Assert.IsTrue(sw2.ElapsedMilliseconds >= 3998, msg);
            Assert.IsTrue(sw2.ElapsedMilliseconds < 4600, msg);

            Assert.IsTrue(sw3.ElapsedMilliseconds >= 2998, msg);
            Assert.IsTrue(sw3.ElapsedMilliseconds < 3600, msg);
        }

        private async Task TestNamedCriticalSection2()
        {
            Guid id = Guid.NewGuid();

            var tasks = Enumerable.Range(0, 5).Select(_ => {

                var sw = new Stopwatch(); 
                sw.Start();

                return Client
                    .GetAsync($"{NamedCriticalSectionRule.UriPattern}?id={id}")
                    .ContinueWith(_ =>
                    {
                        sw.Stop();
                        return sw.ElapsedMilliseconds;
                    });
            });

            var times = await Task.WhenAll(tasks);

            string msg = "Milliseconds: " + string.Join(", ", times);
            Trace.WriteLine(msg);

            Assert.IsTrue(times.SingleOrDefault(t => t >= 2998 && t < 3200) > 0, msg);
            Assert.IsTrue(times.SingleOrDefault(t => t >= 5997 && t < 6300) > 0, msg);
            Assert.IsTrue(times.SingleOrDefault(t => t >= 8997 && t < 9400) > 0, msg);
            Assert.IsTrue(times.SingleOrDefault(t => t >= 11997 && t < 12500) > 0, msg);
            Assert.IsTrue(times.SingleOrDefault(t => t >= 14997 && t < 15600) > 0, msg);
        }

        private async Task TestSemaphore()
        {
            Guid id = Guid.NewGuid();

            var successfulCallTasks = Enumerable.Range(0, 2).Select(_ => Client.GetAsync($"{SemaphoreRule.UriPattern}?id={id}")).ToArray();

            await Task.Delay(TimeSpan.FromMilliseconds(300));

            var throttledCallTasks = Enumerable.Range(0, 5).Select(_ => Client.GetAsync($"{SemaphoreRule.UriPattern}?id={id}")).ToArray();

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
                    .GetAsync($"{DelayedFixedWindowRule.UriPattern}?id={id}")
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

            for(int i = 0; i < 3; i++)
            {
                var tasks = Enumerable.Range(0, 5).Select(_ => {

                    return Client
                        .GetAsync($"{FixedWindowFailingMethodRule.UriPattern}?id={id}")
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

        private async Task TestSemaphoreCrushingMethod()
        {
            Guid id = Guid.NewGuid();

            // First call
            var sw1 = new Stopwatch(); sw1.Start();
            var task1 = Client.GetAsync($"{SemaphoreCrushingMethodRule.UriPattern}?id={id}").ContinueWith(t => { sw1.Stop(); return t.Result.StatusCode; });

            await Task.Delay(TimeSpan.FromMilliseconds(100));

            // Second call
            var sw2 = new Stopwatch(); sw2.Start();
            var task2 = Client.GetAsync($"{SemaphoreCrushingMethodRule.UriPattern}?id={id}").ContinueWith(t => { sw2.Stop(); return t.Result.StatusCode; });

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
            var result = await Client.GetAsync($"{SlidingWindowRule.UriPattern}?id={id}");

            Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);

            // Now keep trying for 6 seconds
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(300);

                result = await Client.GetAsync($"{SlidingWindowRule.UriPattern}?id={id}");

                Assert.AreEqual(HttpStatusCode.TooManyRequests, result.StatusCode);
            }

            // Now waiting till the window expires entirely
            await Task.Delay(4000);

            // Should succeed again
            result = await Client.GetAsync($"{SlidingWindowRule.UriPattern}?id={id}");

            Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
        }
    }
}