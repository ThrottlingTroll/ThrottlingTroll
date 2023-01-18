using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

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
        /// Regex patterns of URLs to be whitelisted (exempt from Rules)
        /// </summary>
        public IList<string> WhiteList { get; set; }

        internal List<Regex> WhiteListRegexes
        {
            get
            {
                if (this._whiteListRegexes == null)
                {
                    if (this.WhiteList == null)
                    {
                        this._whiteListRegexes = new List<Regex>();
                    }
                    else
                    {
                        this._whiteListRegexes = this.WhiteList
                            .Select(url => new Regex(url, RegexOptions.IgnoreCase | RegexOptions.Compiled))
                            .ToList();
                    }
                }

                return this._whiteListRegexes;
            }
        }

        private List<Regex> _whiteListRegexes;
    }

    /// <summary>
    /// ThrottlingTroll Egress Throttling Configuration.
    /// </summary>
    public class ThrottlingTrollEgressConfig : ThrottlingTrollConfig
    {
        /// <summary>
        /// When set to true, 429 TooManyRequest responses are automatically propagated from 
        /// </summary>
        public bool PropagateToIngress { get; set; }
    }
}