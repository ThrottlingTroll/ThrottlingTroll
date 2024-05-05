using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("ThrottlingTroll.AzureFunctionsAspNet.Tests")]

namespace ThrottlingTroll
{
    /// <summary>
    /// Extension methods for configuring ThrottlingTrollMiddleware.
    /// </summary>
    public static class ThrottlingTrollExtensions
    {
        /// <summary>
        /// Configures ThrottlingTroll ingress throttling
        /// </summary>
        public static IFunctionsWorkerApplicationBuilder UseThrottlingTroll(this IFunctionsWorkerApplicationBuilder builder, Action<ThrottlingTrollOptions> options = null)
        {
            return builder.UseThrottlingTroll(options == null ? null : (ctx, opt) => options(opt));
        }

        /// <summary>
        /// Configures ThrottlingTroll ingress throttling
        /// </summary>
        public static IFunctionsWorkerApplicationBuilder UseThrottlingTroll(this IFunctionsWorkerApplicationBuilder builder, Action<FunctionContext, ThrottlingTrollOptions> options)
        {
            // Need to create this instance here, so that Assemblies are correctly initialized.
            var opt = new ThrottlingTrollOptions
            {
                Assemblies = new List<Assembly> { Assembly.GetCallingAssembly() }
            };

            // Need to create a single instance, yet still allow for multiple copies of ThrottlingTrollMiddleware with different settings
            var lockObject = new object();
            ThrottlingTrollMiddleware middleware = null;

            return builder.UseWhen
            (
                (FunctionContext context) =>
                {
                    // This middleware is only for http trigger invocations.
                    return context
                        .FunctionDefinition
                        .InputBindings
                        .Values
                        .First(a => a.Type.EndsWith("Trigger"))
                        .Type == "httpTrigger";
                },
                async (FunctionContext context, Func<Task> next) =>
                {
                    // To initialize ThrottlingTrollMiddleware we need access to context.InstanceServices (the DI container),
                    // and it is only here when we get one.
                    // So that's why all the complexity with double-checked locking etc.

                    if (middleware == null)
                    {
                        lock (lockObject)
                        {
                            if (middleware == null)
                            {
                                if (options != null)
                                {
                                    options(context, opt);
                                }

                                middleware = CreateMiddleware(context, opt);
                            }
                        }
                    }

                    await middleware.Invoke(context, next);
                }
            );
        }

        private static ThrottlingTrollMiddleware CreateMiddleware(FunctionContext context, ThrottlingTrollOptions opt)
        {
            opt.GetConfigFunc = ThrottlingTrollCoreExtensions.MergeAllConfigSources(opt.Config, CollectDeclarativeConfig(opt.Assemblies), opt.GetConfigFunc, context.InstanceServices);

            if (opt.Log == null)
            {
                var logger = context.InstanceServices.GetService<ILogger<ThrottlingTroll>>();
                opt.Log = logger == null ? null : (l, s) => logger.Log(l, s);
            }

            opt.CounterStore ??= context.InstanceServices.GetService<ICounterStore>() ?? new MemoryCacheCounterStore();

            return new ThrottlingTrollMiddleware(opt);
        }

        private static ThrottlingTrollConfig CollectDeclarativeConfig(List<Assembly> assemblies)
        {
            var rules = new List<ThrottlingTrollRule>();

            var allMethods = assemblies
                .SelectMany(a => a.DefinedTypes)
                .Where(t => t.IsClass)
                .SelectMany(c => c.GetMethods());

            foreach (var methodInfo in allMethods)
            {
                var funcAttribute = methodInfo.GetCustomAttributes<FunctionAttribute>().FirstOrDefault();
                if (funcAttribute == null)
                {
                    continue;
                }

                var httpTriggerAttribute = methodInfo
                    .GetParameters()
                    .SelectMany(p => p.GetCustomAttributes<HttpTriggerAttribute>())
                    .FirstOrDefault();
                if (httpTriggerAttribute == null)
                {
                    continue;
                }

                foreach (var trollAttribute in methodInfo.GetCustomAttributes<ThrottlingTrollAttribute>())
                {
                    rules.Add(
                        trollAttribute.ToThrottlingTrollRule(
                            GetUriPattern(funcAttribute, httpTriggerAttribute),
                            httpTriggerAttribute.Methods?.Length > 0 ? string.Join(',', httpTriggerAttribute.Methods) : null
                        )
                    );
                }
            }

            return rules.Count > 0 ? new ThrottlingTrollConfig { Rules = rules } : null;
        }

        // Note that '{' is a special character in regex (that's why it is escaped here), while '}' is _not_.
        private static readonly Regex RouteParamsRegex = new Regex("\\\\{[\\w:\\?]*?}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string GetUriPattern(FunctionAttribute funcAttribute, HttpTriggerAttribute triggerAttribute)
        {
            string result;

            if (string.IsNullOrEmpty(triggerAttribute.Route))
            {
                result = funcAttribute.Name;
            }
            else
            {
                // escaping all other regex special characters
                result = Regex.Escape(triggerAttribute.Route.TrimStart('/'));

                result = RouteParamsRegex
                    // Replacing HTTP route parameters with wildcards. At this point curly brackets will already be escaped, so that's why they're escaped in RouteParamsRegex.
                    .Replace(result, ".*")
                ;
            }

            return $"/{result}";
        }
    }
}
