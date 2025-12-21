
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// Abstraction layer for ingress HTTP responses
    /// </summary>
    public abstract class IngressHttpResponseProxyBase
    {
        /// <inheritdoc />
        public bool ShouldContinueAsNormal { get; set; }

        /// <summary>
        /// Does whatever it takes for the response to be returned by the called func.
        /// </summary>
        internal abstract void Apply();

        internal abstract Task ConstructResponse(
            List<LimitCheckResult> checkList,
            IHttpRequestProxy requestProxy,
            Func<Task> callNextOnce,
            Func<List<LimitCheckResult>, IHttpRequestProxy, IHttpResponseProxy, CancellationToken, Task> responseFabric,
            CancellationToken cancellationToken);

        /// <summary>
        /// Executes an action when request is finished being processed
        /// </summary>
        internal abstract Task OnResponseCompleted(Func<Task> action);
    }
}