using StackExchange.Redis;
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

            // </ThrottlingTroll Ingress Configuration>


            app.Run();
        }
    }
}