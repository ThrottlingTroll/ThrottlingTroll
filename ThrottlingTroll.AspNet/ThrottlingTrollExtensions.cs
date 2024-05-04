using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

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
            // Need to create this instance here, so that Assemblies are correctly initialized.
            var opt = new ThrottlingTrollOptions
            {
                Assemblies = new List<Assembly> { Assembly.GetCallingAssembly() }
            };

            if (options != null)
            {
                options(opt);
            }

            opt.GetConfigFunc = ThrottlingTrollCoreExtensions.MergeAllConfigSources(opt.Config, CollectDeclarativeConfig(opt.Assemblies), opt.GetConfigFunc, builder.ApplicationServices);

            if (opt.Log == null)
            {
                var logger = builder.ApplicationServices.GetService<ILogger<ThrottlingTroll>>();
                opt.Log = logger == null ? null : (l, s) => logger.Log(l, s);
            }

            // TODO: move default counter store creation into ThrottlingTroll
            opt.CounterStore ??= builder.ApplicationServices.GetService<ICounterStore>() ?? new MemoryCacheCounterStore();

            return builder.UseMiddleware<ThrottlingTrollMiddleware>(opt);
        }

        private static ThrottlingTrollConfig CollectDeclarativeConfig(List<Assembly> assemblies)
        {
            var rules = new List<ThrottlingTrollRule>();

            var allClasses = assemblies
                .SelectMany(a => a.DefinedTypes)
                .Where(t => t.IsClass)
            ;

            foreach (var classInfo in allClasses)
            {
                // Controller-level rules
                foreach (var trollAttribute in classInfo.GetCustomAttributes<ThrottlingTrollAttribute>())
                {
                    rules.Add(trollAttribute.ToThrottlingTrollRule(GetUriPatternForController(classInfo)));
                }

                // Method-level rules
                foreach (var methodInfo in classInfo.GetMethods())
                {
                    foreach (var trollAttribute in methodInfo.GetCustomAttributes<ThrottlingTrollAttribute>())
                    {
                        rules.Add(trollAttribute.ToThrottlingTrollRule(GetUriPatternForControllerMethod(classInfo, methodInfo)));
                    }
                }
            }

            return rules.Count > 0 ? new ThrottlingTrollConfig { Rules = rules } : null;
        }

        // Note that '{' is a special character in regex (that's why it is escaped here), while '}' is _not_.
        private static readonly Regex RouteParamsRegex = new Regex("\\\\{[\\w:\\?]*?}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string GetUriPatternForController(TypeInfo classInfo, string action = ".*")
        {
            string result = null;
            string controllerName = classInfo.Name.TrimSuffix("Controller");

            var routeAttribute = classInfo.GetCustomAttributes<RouteAttribute>().FirstOrDefault();
            if (routeAttribute?.Template == null) 
            {
                // Just returning the controller name
                result = controllerName;
            }
            else
            {
                // escaping all other regex special characters
                result = Regex.Escape(routeAttribute.Template.TrimStart('/'));

                result = RouteParamsRegex
                // Replacing HTTP route parameters with wildcards. At this point curly brackets will already be escaped, so that's why they're escaped in RouteParamsRegex.
                    .Replace(result, ".*")
                    // Replacing [area] token with a wildcard. Note that '[' is a special character in regex (that's why it is escaped here), while ']' is _not_.
                    .Replace("\\[area]", ".*")
                    // Replacing [controller] token. Note that '[' is a special character in regex (that's why it is escaped here), while ']' is _not_.
                    .Replace("\\[controller]", controllerName)
                    // Replacing [action] token. Note that '[' is a special character in regex (that's why it is escaped here), while ']' is _not_.
                    .Replace("\\[action]", action)
                ;
            }

            return $"/{result}";
        }

        private static string GetUriPatternForControllerMethod(TypeInfo classInfo, MethodInfo methodInfo)
        {
            string controllerName = classInfo.Name.TrimSuffix("Controller");
            string actionName = methodInfo.Name;

            var routeAttribute = methodInfo
                .GetCustomAttributes<HttpMethodAttribute>()
                .Cast<IRouteTemplateProvider>()
                .Concat(methodInfo.GetCustomAttributes<RouteAttribute>())
                // There can be multiple HttpMethodAttributes defined, and we're only interested in the one with a Route
                .Where(at => !string.IsNullOrEmpty(at.Template))
                .FirstOrDefault();

            if (routeAttribute == null)
            {
                // If a method is marked with ThrottlingTrollAttribute, and that method does not have a route, and its controller has,
                // this combination would apply method's limit to the entire controller. We do not want that to happen.
                if (classInfo.GetCustomAttributes<RouteAttribute>().Any())
                {
                    throw new InvalidOperationException($"ThrottlingTroll rule defined on {controllerName}.{actionName} action would override the rule defined on the controller level. This combination is not supported. Either define a route for this action, or remove ThrottlingTrollAttribute from it.");
                }

                // Route-less methods in controllers match the controller's route
                return GetUriPatternForController(classInfo, actionName);
            }

            string result = routeAttribute.Template;

            // Prepending controller's route, if any
            var controllerRouteAttribute = classInfo.GetCustomAttributes<RouteAttribute>().FirstOrDefault();
            if (controllerRouteAttribute != null) 
            {
                result = $"{controllerRouteAttribute.Template.Trim('/')}/{result.TrimStart('/')}";
            }

            // escaping all other regex special characters
            result = Regex.Escape(result);

            result = RouteParamsRegex
                // Replacing HTTP route parameters with wildcards. At this point curly brackets will already be escaped, so that's why they're escaped in RouteParamsRegex.
                .Replace(result, ".*")
                // Replacing [area] token with a wildcard. Note that '[' is a special character in regex (that's why it is escaped here), while ']' is _not_.
                .Replace("\\[area]", ".*")
                // Replacing [controller] token. Note that '[' is a special character in regex (that's why it is escaped here), while ']' is _not_.
                .Replace("\\[controller]", controllerName)
                // Replacing [action] token. Note that '[' is a special character in regex (that's why it is escaped here), while ']' is _not_.
                .Replace("\\[action]", actionName)
            ;

            return $"/{result}";
        }
    }
}