using System.Collections.Generic;

namespace ThrottlingTroll
{
    /// <summary>
    /// ThrottlingTroll Configuration.
    /// </summary>
    public class ThrottlingTrollConfig
    {
        /// <summary>
        /// Rate Limiting Rules
        /// </summary>
        public IList<ThrottlingTrollRule> Rules { get; set; }

        /// <summary>
        /// Optional name for this setup. Used as a prefix for rate counter cache keys.
        /// When multiple services store their counters in the same cache instance,
        /// specify some service name in this property, so that services do not interfere.
        /// </summary>
        public string UniqueName { get; set; }

        /// <summary>
        /// Requests to be whitelisted (exempt from Rules)
        /// </summary>
        public IList<RequestFilter> WhiteList { get; set; }

        /// <summary>
        /// Default ctor
        /// </summary>
        public ThrottlingTrollConfig()
        {
        }

        /// <summary>
        /// Merging ctor
        /// </summary>
        public ThrottlingTrollConfig(ThrottlingTrollConfig that)
        {
            if (that == null) 
            {
                return;
            }

            if (!string.IsNullOrEmpty(that.UniqueName)) 
            {
                this.UniqueName = that.UniqueName;
            }

            this.Rules = ThrottlingTrollCoreExtensions.UnionOf(this.Rules, that.Rules);
            this.WhiteList = ThrottlingTrollCoreExtensions.UnionOf(this.WhiteList, that.WhiteList);
        }
    }

    /// <summary>
    /// ThrottlingTroll Egress Throttling Configuration.
    /// </summary>
    public class ThrottlingTrollEgressConfig : ThrottlingTrollConfig
    {
        /// <summary>
        /// When set to true, 429 TooManyRequest responses are automatically propagated from egress to ingress
        /// </summary>
        public bool PropagateToIngress { get; set; }
    }
}
