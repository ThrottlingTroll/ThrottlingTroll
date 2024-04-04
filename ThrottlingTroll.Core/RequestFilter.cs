using System.Text.RegularExpressions;
using System;
using System.Linq;

namespace ThrottlingTroll
{
    /// <summary>
    /// Defines a Request Filter (a condition that requests must match in order to be throttled or whitelisted)
    /// </summary>
    public class RequestFilter
    {
        /// <summary>
        /// A Regex pattern to match request URI against. Empty string or null means any URI.
        /// </summary>
        public string UriPattern { get; set; }

        /// <summary>
        /// Comma-separated request's HTTP methods. E.g. "GET,POST". Empty string or null means any method.
        /// </summary>
        public string Method { get; set; }

        /// <summary>
        /// Request's HTTP header to check. If specified, the rule will only apply to requests with this header set to <see cref="HeaderValue"/>.
        /// If <see cref="HeaderName"/> is specified and <see cref="HeaderValue"/> is not - that matches requests with any value in that header.
        /// </summary>
        public string HeaderName { get; set; }

        /// <summary>
        /// Value for HTTP header identified by <see cref="HeaderName"/>. The rule will only apply to requests with that header set to this value.
        /// If <see cref="HeaderName"/> is specified and <see cref="HeaderValue"/> is not - that matches requests with any value in that header.
        /// </summary>
        public string HeaderValue { get; set; }

        /// <summary>
        /// Request's custom Identity ID. If specified, the rule will only apply to requests with this Identity ID. Identity IDs are extacted with <see cref="IdentityIdExtractor"/>.
        /// </summary>
        public string IdentityId { get; set; }

        /// <summary>
        /// Identity ID extraction routine to be used for extracting Identity IDs from requests.
        /// Overrides <see cref="ThrottlingTrollOptions.IdentityIdExtractor"/>.
        /// </summary>
        public Func<IHttpRequestProxy, string> IdentityIdExtractor { get; set; }

        /// <summary>
        /// Checks whether given request matches this filter
        /// </summary>
        internal bool IsMatch(IHttpRequestProxy request)
        {
            return this.IsUrlMatch(request) &&
                this.IsMethodMatch(request) &&
                this.IsHeaderMatch(request) &&
                this.IsIdentityMatch(request);
        }

        /// <summary>
        /// <see cref="UriPattern"/> converted into <see cref="Regex"/>
        /// </summary>
        protected Regex UrlRegex
        {
            get
            {
                if (string.IsNullOrEmpty(this.UriPattern))
                {
                    return null;
                }

                if (this._uriRegex == null)
                {
                    this._uriRegex = new Regex(this.UriPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                }

                return this._uriRegex;
            }
        }

        private bool IsUrlMatch(IHttpRequestProxy request)
        {
            if (this.UrlRegex == null)
            {
                return true;
            }

            return this.UrlRegex.IsMatch(request.Uri);
        }

        private bool IsMethodMatch(IHttpRequestProxy request)
        {
            if (string.IsNullOrEmpty(this.Method))
            {
                return true;
            }

            return this.Method
                .ToLower()
                .Split(',')
                .Select(s => s.Trim())
                .Contains(request.Method.ToLower());
        }

        private bool IsHeaderMatch(IHttpRequestProxy request)
        {
            if (string.IsNullOrEmpty(this.HeaderName))
            {
                return true;
            }

            if (!request.Headers.ContainsKey(this.HeaderName))
            {
                return false;
            }

            if (string.IsNullOrEmpty(this.HeaderValue))
            {
                return true;
            }

            var headerValue = request.Headers[this.HeaderName];

            return headerValue == this.HeaderValue;
        }

        private bool IsIdentityMatch(IHttpRequestProxy request)
        {
            if (this.IdentityIdExtractor == null || string.IsNullOrEmpty(this.IdentityId))
            {
                return true;
            }

            var identityId = this.IdentityIdExtractor(request);

            return identityId.Contains(this.IdentityId);
        }

        private Regex _uriRegex;
    }
}
