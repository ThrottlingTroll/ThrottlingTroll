using Microsoft.Net.Http.Headers;
using System.Text.Json;
using ThrottlingTroll;

namespace ThrottlingTrollSampleWeb
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(options =>
            {
                options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, "ThrottlingTrollSampleWeb.xml"));
            });


            // <ThrottlingTroll Egress Configuration>

            // Configuring a named HttpClient for egress throttling. Rules and limits taken from appsettings.json
            builder.Services.AddHttpClient("my-throttled-httpclient").AddThrottlingTrollMessageHandler();

            // Configuring a named HttpClient that does automatic retries with respect to Retry-After response header
            builder.Services.AddHttpClient("my-retrying-httpclient").AddThrottlingTrollMessageHandler(options =>
            {
                options.ResponseFabric = async (limitExceededResult, requestProxy, responseProxy, cancelToken) =>
                {
                    var egressResponse = (IEgressHttpResponseProxy)responseProxy;

                    egressResponse.ShouldRetry = true;
                };
            });

            // </ThrottlingTroll Egress Configuration>


            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();

            app.MapControllers();


            // <ThrottlingTroll Ingress Configuration>

            // Normally you'll configure ThrottlingTroll just once, but it's OK to have multiple 
            // middleware instances, with different settings. We're doing it here for demo purposes only.

            // Simplest form. Loads config from appsettings.json and uses MemoryCacheCounterStore by default.
            app.UseThrottlingTroll();

            // Static programmatic configuration
            app.UseThrottlingTroll(options =>
            {
                // Here is how to enable storing rate counters in Redis. You'll also need to add a singleton IConnectionMultiplexer instance beforehand.
                // options.CounterStore = new RedisCounterStore(app.Services.GetRequiredService<IConnectionMultiplexer>());

                options.Config = new ThrottlingTrollConfig
                {
                    Rules = new[]
                    {
                        new ThrottlingTrollRule
                        {
                            UriPattern = "/fixed-window-1-request-per-2-seconds-configured-programmatically",
                            LimitMethod = new FixedWindowRateLimitMethod
                            {
                                PermitLimit = 1,
                                IntervalInSeconds = 2
                            }
                        }
                    },

                    // Specifying UniqueName is needed when multiple services store their
                    // rate limit counters in the same cache instance, to prevent those services
                    // from corrupting each other's counters. Otherwise you can skip it.
                    UniqueName = "MyThrottledService1"
                };
            });

            // Dynamic programmatic configuration. Allows to adjust rules and limits without restarting the service.
            app.UseThrottlingTroll(options =>
            {
                options.GetConfigFunc = async () =>
                {
                    // Loading settings from a custom file. You can instead load them from a database
                    // or from anywhere else.

                    string ruleFileName = Path.Combine(AppContext.BaseDirectory, "my-dynamic-throttling-rule.json");

                    string ruleJson = await File.ReadAllTextAsync(ruleFileName);

                    var rule = JsonSerializer.Deserialize<ThrottlingTrollRule>(ruleJson);

                    return new ThrottlingTrollConfig
                    {
                        Rules = new[] { rule }
                    };
                };

                // The above function will be periodically called every 5 seconds
                options.IntervalToReloadConfigInSeconds = 5;
            });

            // Demonstrates how to use custom response fabrics
            app.UseThrottlingTroll(options =>
            {
                options.Config = new ThrottlingTrollConfig
                {
                    Rules = new[]
                    {
                        new ThrottlingTrollRule
                        {
                            UriPattern = "/fixed-window-1-request-per-2-seconds-response-fabric",
                            LimitMethod = new FixedWindowRateLimitMethod
                            {
                                PermitLimit = 1,
                                IntervalInSeconds = 2
                            }
                        }
                    }
                };

                // Custom response fabric, returns 400 BadRequest + some custom content
                options.ResponseFabric = async (limitExceededResult, requestProxy, responseProxy, requestAborted) =>
                {
                    responseProxy.StatusCode = StatusCodes.Status400BadRequest;

                    responseProxy.SetHttpHeader(HeaderNames.RetryAfter, limitExceededResult.RetryAfterHeaderValue);

                    await responseProxy.WriteAsync("Too many requests. Try again later.");
                };
            });

            // Demonstrates how to delay the response instead of returning 429
            app.UseThrottlingTroll(options =>
            {
                options.Config = new ThrottlingTrollConfig
                {
                    Rules = new[]
                    {
                        new ThrottlingTrollRule
                        {
                            UriPattern = "/fixed-window-1-request-per-2-seconds-delayed-response",
                            LimitMethod = new FixedWindowRateLimitMethod
                            {
                                PermitLimit = 1,
                                IntervalInSeconds = 2
                            }
                        }
                    }
                };

                // Custom response fabric, impedes the normal response for 3 seconds
                options.ResponseFabric = async (limitExceededResult, requestProxy, responseProxy, requestAborted) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(3));

                    var ingressResponse = (IIngressHttpResponseProxy)responseProxy;
                    ingressResponse.ShouldContinueAsNormal = true;
                };
            });

            // Demonstrates how to use identity extractors
            app.UseThrottlingTroll(options =>
            {
                options.Config = new ThrottlingTrollConfig
                {
                    Rules = new[]
                    {
                        new ThrottlingTrollRule
                        {
                            UriPattern = "/fixed-window-3-requests-per-15-seconds-per-each-api-key",
                            LimitMethod = new FixedWindowRateLimitMethod
                            {
                                PermitLimit = 3,
                                IntervalInSeconds = 15
                            },

                            IdentityIdExtractor = request =>
                            {
                                // Identifying clients by their api-key
                                return ((IIncomingHttpRequestProxy)request).Request.Query["api-key"];
                            }
                        }
                    }
                };
            });

            // </ThrottlingTroll Ingress Configuration>


            app.Run();
        }
    }
}