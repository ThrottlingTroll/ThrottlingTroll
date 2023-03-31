using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using ThrottlingTroll;

var builder = new HostBuilder();

// Need to explicitly load configuration from host.json
builder.ConfigureAppConfiguration(configBuilder => {

    configBuilder.AddJsonFile("host.json", optional: false, reloadOnChange: true);
});



// <ThrottlingTroll Ingress Configuration>

// Normally you'll configure ThrottlingTroll just once, but it's OK to have multiple 
// middleware instances, with different settings. We're doing it here for demo purposes only.

// Simplest form. Loads config from host.json and uses MemoryCacheCounterStore by default.
builder.UseThrottlingTroll();

// </ThrottlingTroll Ingress Configuration>



var host = builder.Build();
host.Run();
