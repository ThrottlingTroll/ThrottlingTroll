using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;

namespace ThrottlingTroll
{
    /// <summary>
    /// Extension methods for configuring ThrottlingTrollMiddleware
    /// </summary>
    public static class ThrottlingTrollExtensions
    {
        /// <summary>
        /// Configures ThrottlingTroll ingress throttling
        /// </summary>
        public static IApplicationBuilder UseThrottlingTroll(this IApplicationBuilder builder, Action<ThrottlingTrollOptions> options = null)
        {
            var opt = new ThrottlingTrollOptions();

            if (options != null)
            {
                options(opt);
            }

            if (opt.GetConfigFunc == null)
            {
                if (opt.Config == null)
                {
                    // Trying to read config from settings

                    var configSection = ThrottlingTrollConfig.FromConfigSection(builder.ApplicationServices);

                    opt.GetConfigFunc = async () => configSection;
                }
                else
                {
                    opt.GetConfigFunc = async () => opt.Config;
                }
            }

            if (opt.Log == null)
            {
                var logger = builder.ApplicationServices.GetService<ILogger<ThrottlingTroll>>();
                opt.Log = logger == null ? null : (l, s) => logger.Log(l, s);
            }


            if (opt.CounterStore == null)
            {
                opt.CounterStore = builder.GetOrCreateThrottlingTrollCounterStore();
            }

            return builder.UseMiddleware<ThrottlingTrollMiddleware>(opt);
        }

        private static ICounterStore GetOrCreateThrottlingTrollCounterStore(this IApplicationBuilder builder)
        {
            var counterStore = builder.ApplicationServices.GetService<ICounterStore>();

            if (counterStore == null)
            {
                counterStore = new MemoryCacheCounterStore();
            }

            return counterStore;
        }

        private const string ConfigSectionName = "ThrottlingTrollIngress";
    }
}