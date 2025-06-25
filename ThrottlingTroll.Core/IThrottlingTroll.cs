
using System;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// Distributed rate limiter/throttler/circuit breaker for generic usage in any .NET application.
    /// </summary>
    public interface IThrottlingTroll
    {
        /// <summary>
        /// Applies limits to a given arbitrary piece of code. When limit is exceeded, calls onLimitExceeded.
        /// </summary>
        /// <typeparam name="T">Return type of your code.</typeparam>
        /// <param name="todo">Piece of code to be limited</param>
        /// <param name="onLimitExceeded">
        /// Will be called when a limit is exceeded. A <see cref="ThrottlingTrollContext"/> will be passed to it,
        /// so that your code can see which rule(s) were exceeded.
        /// </param>
        /// <param name="methodName">
        /// Optional arbitrary string identifying this particular execution flow. Will be passed to 
        /// <see cref="IHttpRequestProxy.Method"/> property, so you can configure limits that will be applied
        /// individually, based on this value. You can then also use it in your custom identityIdExtractors
        /// and costExtractors.
        /// <returns>Awaitable task</returns>
        Task<T> WithThrottlingTroll<T>(Func<Task<T>> todo, Func<ThrottlingTrollContext, Task<T>> onLimitExceeded, string methodName = null);

        /// <summary>
        /// Applies limits to a given arbitrary piece of code. Expects an instance of <see cref="IHttpRequestProxy"/> to be passed.
        /// You would need to make your custom implementation of it, which matches your scenario.
        /// </summary>
        /// <typeparam name="T">Return type of your code.</typeparam>
        /// <param name="requestProxy">An instance of <see cref="IHttpRequestProxy"/></param>
        /// <param name="todo"></param>
        /// <param name="onLimitExceeded"></param>
        /// <returns></returns>
        Task<T> WithThrottlingTroll<T>(IHttpRequestProxy requestProxy, Func<Task<T>> todo, Func<ThrottlingTrollContext, Task<T>> onLimitExceeded);

        /// <summary>
        /// Returns the current <see cref="ThrottlingTrollConfig"/> snapshot, for your code's reference.
        /// </summary>
        Task<ThrottlingTrollConfig> GetCurrentConfig();
    }
}