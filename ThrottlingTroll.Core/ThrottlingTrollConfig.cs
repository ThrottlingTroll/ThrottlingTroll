using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
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
        /// Requests to be allowlisted (exempt from Rules)
        /// </summary>
        [Obsolete("Use AllowList instead.")]
        public IList<RequestFilter> WhiteList { get => this.AllowList; set => this.AllowList = value; }

        /// <summary>
        /// Requests to be allowlisted (exempt from Rules)
        /// </summary>
        public IList<RequestFilter> AllowList { get; set; }

        /// <summary>
        /// Merges two ThrottlingTrollConfig objects (by concatenating <see cref="Rules"/> and <see cref="AllowList"/> fields)
        /// </summary>
        public ThrottlingTrollConfig MergeWith(ThrottlingTrollConfig that)
        {
            if (that == null) 
            {
                return this;
            }

            if (!string.IsNullOrEmpty(that.UniqueName)) 
            {
                this.UniqueName = that.UniqueName;
            }

            this.Rules = ThrottlingTrollCoreExtensions.UnionOf(this.Rules, that.Rules);
            this.AllowList = ThrottlingTrollCoreExtensions.UnionOf(this.AllowList, that.AllowList);

            return this;
        }

        /// <summary>
        /// Creates an instance of <see cref="ThrottlingTrollConfig"/> out of config settings.
        /// </summary>
        public static ThrottlingTrollConfig FromConfigSection(IServiceProvider provider, string sectionName)
        {
            var config = provider.GetService<IConfiguration>();

            var section = config?.GetSection(sectionName);

            var result = section?.Get<ThrottlingTrollConfig>();

            return result;
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
