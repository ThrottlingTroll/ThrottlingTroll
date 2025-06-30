using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

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

            opt.GetConfigFunc = ThrottlingTrollCoreExtensions.MergeAllConfigSources(
                opt.Config,
                CollectDeclarativeConfig(opt.Assemblies),
                opt.GetConfigFunc,
                builder.ApplicationServices,
                opt.ConfigSectionName
            );

            if (opt.Log == null)
            {
                var logger = builder.ApplicationServices.GetService<ILogger<ThrottlingTrollCore>>();
                opt.Log = logger == null ? null : (l, s) => logger.Log(l, s);
            }

            // TODO: move default counter store creation into ThrottlingTroll
            opt.CounterStore ??= builder.ApplicationServices.GetService<ICounterStore>() ?? new MemoryCacheCounterStore();

            return builder.UseMiddleware<ThrottlingTrollMiddleware>(opt);
        }

        /// <summary>
        /// Returns the current (active) ThrottlingTroll configuration (all rules and limits collected from all config sources)
        /// </summary>
        public static List<ThrottlingTrollConfig> GetThrottlingTrollConfig(this HttpContext context)
        {
            return (List<ThrottlingTrollConfig>)context.Items[ThrottlingTrollCore.ThrottlingTrollConfigsContextKey];
        }

        /// <summary>
        /// Returns the results of checking ThrottlingTroll rules (all that apply to current request).
        /// Returns null when ThrottlingTroll experienced an internal failure.
        /// </summary>
        public static List<LimitCheckResult> GetThrottlingTrollLimitCheckResults(this HttpContext context)
        {
            return (List<LimitCheckResult>)context.Items[ThrottlingTrollCore.LimitCheckResultsContextKey];
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
                bool isRazorPage = classInfo.IsSubclassOf(typeof(PageModel));

                // Controller-level rules
                foreach (var trollAttribute in classInfo.GetCustomAttributes<ThrottlingTrollAttribute>())
                {
                    var rule = trollAttribute.ToThrottlingTrollRule(isRazorPage ? GetUriPatternForRazorPage(classInfo) : GetUriPatternForController(classInfo));

                    rules.Add(rule);
                }

                // Method-level rules
                foreach (var methodInfo in classInfo.GetMethods())
                {
                    foreach (var trollAttribute in methodInfo.GetCustomAttributes<ThrottlingTrollAttribute>())
                    {
                        var rule = isRazorPage ?
                            trollAttribute.ToThrottlingTrollRule(GetUriPatternForRazorPageHandler(classInfo, methodInfo), GetHttpVerbsForRazorPageHandler(methodInfo)) :
                            trollAttribute.ToThrottlingTrollRule(GetUriPatternForControllerMethod(classInfo, methodInfo), GetHttpVerbsForControllerMethod(methodInfo))
                        ;

                        rules.Add(rule);
                    }
                }
            }

            return rules.Count > 0 ? new ThrottlingTrollConfig { Rules = rules } : null;
        }

        private static string GetUriPatternForController(TypeInfo classInfo, string action = ".*")
        {
            string controllerName = classInfo.Name.TrimSuffix("Controller");

            var routeAttributes = classInfo.GetCustomAttributes<RouteAttribute>().Where(at => at.Template != null).ToArray();

            if (routeAttributes.Length <= 0)
            {
                // Just returning the controller name
                return $"/{controllerName}";
            }

            string result = null;
            foreach (var routeAttribute in routeAttributes)
            {
                // Trimming both "/" and "~/" from the start
                string template = routeAttribute.Template.TrimStart('~', '/');

                template = EscapeRoute(template, controllerName, action);

                // We need (potentially) multiple routes to become the same rule (not separate rules), so using regex OR operator
                result = result == null ? $"/{template}" : $"{result}|/{template}";
            }

            return result;
        }

        private static string GetUriPatternForRazorPage(TypeInfo classInfo)
        {
            string pageName = classInfo.Name.TrimSuffix("Model");

            return $"/{pageName}";
        }

        private static string GetUriPatternForControllerMethod(TypeInfo classInfo, MethodInfo methodInfo)
        {
            string controllerName = classInfo.Name.TrimSuffix("Controller");
            string actionName = methodInfo.Name;

            var routeAttributes = methodInfo
                .GetCustomAttributes<HttpMethodAttribute>()
                .Cast<IRouteTemplateProvider>()
                .Concat(methodInfo.GetCustomAttributes<RouteAttribute>())
                // There can be multiple HttpMethodAttributes defined, and we're only interested in the ones with a Route
                .Where(at => !string.IsNullOrEmpty(at.Template))
                .ToArray();

            if (routeAttributes.Length <= 0)
            {
                if (classInfo.GetCustomAttributes<RouteAttribute>().Any())
                {
                    // This seems to be a route-less method of an API controller
                    return GetUriPatternForController(classInfo, actionName);
                }

                // This seems to be an action in an MVC controller
                return $"/{controllerName}/{actionName}";
            }

            string result = null;

            foreach (var routeAttribute in routeAttributes)
            {
                string template = routeAttribute.Template;

                // Slash or tilde+slash means root, so in that case need to skip the controller part
                if (template.StartsWith("/") || template.StartsWith("~/"))
                {
                    // Trimming both "/" and "~/" from the start
                    template = template.TrimStart('~', '/');
                }
                else
                {
                    // Prepending controller's route, if any
                    var controllerRouteAttribute = classInfo.GetCustomAttributes<RouteAttribute>().FirstOrDefault();
                    if (controllerRouteAttribute != null)
                    {
                        template = $"{controllerRouteAttribute.Template.Trim('/')}/{template}";
                    }
                }

                template = EscapeRoute(template, controllerName, actionName);

                // We need (potentially) multiple routes to become the same rule (not separate rules), so using regex OR operator
                result = result == null ? $"/{template}" : $"{result}|/{template}";
            }

            return result;
        }

        private static string GetHttpVerbsForControllerMethod(MethodInfo methodInfo)
        {
            var verbs = methodInfo
                .GetCustomAttributes<HttpMethodAttribute>()
                .SelectMany(at => at.HttpMethods)
                .Distinct(StringComparer.InvariantCultureIgnoreCase)
                .ToArray();

            return verbs.Length > 0 ? string.Join(',', verbs) : null;
        }

        private static readonly Regex RazorPageHandlerMethodRegex = new Regex("^On(GET|POST|PUT|PATCH|DELETE|HEAD|OPTIONS)(\\w*?)(?:Async)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string GetUriPatternForRazorPageHandler(TypeInfo classInfo, MethodInfo methodInfo)
        {
            string pageName = classInfo.Name.TrimSuffix("Model");

            var match = RazorPageHandlerMethodRegex.Match(methodInfo.Name);
            string customHandlerName = match.Success ? match.Groups[2].Value : null;

            return string.IsNullOrEmpty(customHandlerName) ? $"/{pageName}" : $"/{pageName}\\?handler={customHandlerName}";
        }

        private static string GetHttpVerbsForRazorPageHandler(MethodInfo methodInfo)
        {
            var match = RazorPageHandlerMethodRegex.Match(methodInfo.Name);

            return match.Success ? match.Groups[1].Value : null;
        }

        // Note that '{' is a special character in regex (that's why it is escaped here), while '}' is _not_.
        private static readonly Regex RouteParamsRegex = new Regex("\\\\{[\\w:\\?]*?}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string EscapeRoute(string route, string controller, string action)
        {
            // Escaping all other regex special characters. This needs to be done first.
            route = Regex.Escape(route);

            route = RouteParamsRegex
                // Replacing HTTP route parameters with wildcards. At this point curly brackets will already be escaped, so that's why they're escaped in RouteParamsRegex.
                .Replace(route, ".*")
                // Replacing [area] token with a wildcard. Note that '[' is a special character in regex (that's why it is escaped here), while ']' is _not_.
                .Replace("\\[area]", ".*")
                // Replacing [controller] token. Note that '[' is a special character in regex (that's why it is escaped here), while ']' is _not_.
                .Replace("\\[controller]", controller)
                // Replacing [action] token. Note that '[' is a special character in regex (that's why it is escaped here), while ']' is _not_.
                .Replace("\\[action]", action)
            ;

            return route;
        }
    }
}