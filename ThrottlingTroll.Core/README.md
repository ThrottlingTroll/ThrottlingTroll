# ThrottlingTroll.Core

Core rate limiting/throttling/circuit breaking functionality.

Install from NuGet:
```
dotnet add package ThrottlingTroll
```

## How to use in a generic .NET code

Vanilla console app example of using [CircuitBreaker](https://github.com/ThrottlingTroll/ThrottlingTroll/wiki/410.-Rate-Limiting-Algorithms#-circuitbreaker):

```
static IThrottlingTroll ThTr = new ThrottlingTroll.ThrottlingTroll
(
    new ThrottlingTrollConfig
    {
        Rules =
        [
            new ThrottlingTrollRule
            {
                Name = "Circuit breaker",

                LimitMethod = new CircuitBreakerRateLimitMethod
                {
                    PermitLimit = 2,
                    IntervalInSeconds = 10,
                    TrialIntervalInSeconds = 20
                }
            }
        ]
    }

    // Add a distributed counter store to make limits distributed, e.g.:
    //, counterStore: new RedisCounterStore(ConnectionMultiplexer.Connect("localhost:6379"))
);

static async Task MyFailingMethod(ConsoleKey key)
{
    Console.Write("\nMyFailingMethod()...");

    if (key == ConsoleKey.Spacebar)
    {
        throw new Exception("failed");
    }

    Console.WriteLine("succeeded");
}

static async Task Main(string[] args)
{
    Console.WriteLine("'Space' to make MyFailingMethod() fail, any other key to make it succeed:");

    while (true)
    {
        var key = Console.ReadKey().Key;
        try
        {
            await ThTr.WithThrottlingTroll(
                async ctx => await MyFailingMethod(key),
                async ctx =>
                {
                    var trialIntervalInSeconds = ((CircuitBreakerRateLimitMethod)ctx.ExceededLimit.Rule.LimitMethod).TrialIntervalInSeconds;
                    Console.WriteLine($"\n{ctx.ExceededLimit.Rule.Name} is in trial state, wait for {trialIntervalInSeconds} seconds");
                });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}
```
