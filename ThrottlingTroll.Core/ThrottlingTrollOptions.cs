
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// Options for programmatic configuration
    /// </summary>
    public class ThrottlingTrollOptions
    {
        /// <summary>
        /// Static instance of <see cref="ThrottlingTrollConfig"/>
        /// </summary>
        public ThrottlingTrollConfig Config { get; set; }

        /// <summary>
        /// <see cref="ThrottlingTrollConfig"/> getter. Use this to load throttling rules from some storage.
        /// </summary>
        public Func<Task<ThrottlingTrollConfig>> GetConfigFunc { get; set; }

        /// <summary>
        /// When set to > 0, <see cref="GetConfigFunc"/> will be called periodically with this interval.
        /// This allows you to dynamically change throttling rules and limits without restarting the service.
        /// </summary>
        public int IntervalToReloadConfigInSeconds { get; set; }

        /// <summary>
        /// <see cref="ICounterStore"/> implementation, to store counters in.
        /// </summary>
        public ICounterStore CounterStore { get; set; }

        /// <summary>
        /// Identity ID extraction routine to be used for extracting Identity IDs from requests.
        /// </summary>
        public Func<IHttpRequestProxy, string> IdentityIdExtractor { get; set; }

        /// <summary>
        /// Request's cost extraction routine. The default cost (weight) of a request is 1, but this routine allows to override that.
        /// </summary>
        public Func<IHttpRequestProxy, long> CostExtractor { get; set; }

        /// <summary>
        /// Logging utility to use
        /// </summary>
        public Action<LogLevel, string> Log { get; set; }

        /// <summary>
        /// Custom response creation routine.<br/>
        /// Takes <see cref="List{LimitExceededResult}"/> (represents the list of rules the request matched and the corresponding check results),<br/>
        /// <see cref="IHttpRequestProxy"/> (provides info about the ongoing request), <br/> 
        /// <see cref="IHttpResponseProxy"/> (which should be customized by your code) and <br/>
        /// <see cref="CancellationToken"/> (which indicates that the request was aborted from outside)
        /// </summary>
        public Func<List<LimitCheckResult>, IHttpRequestProxy, IHttpResponseProxy, CancellationToken, Task> ResponseFabric { get; set; }
    }
}