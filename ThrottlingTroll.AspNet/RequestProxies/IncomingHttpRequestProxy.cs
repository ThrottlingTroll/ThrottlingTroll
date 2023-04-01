using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using System.Collections.Generic;

namespace ThrottlingTroll
{
    /// <summary>
    /// Abstraction layer on top of <see cref="HttpRequest"/>
    /// </summary>
    public class IncomingHttpRequestProxy : IIncomingHttpRequestProxy
    {
        internal IncomingHttpRequestProxy(HttpRequest request)
        {
            this.Request = request;
        }

        /// <inheritdoc />
        public HttpRequest Request { get; private set; }

        /// <inheritdoc />
        public string Uri
        {
            get
            {
                string path = this.Request.Path.ToString().Trim('/');
                if (!string.IsNullOrEmpty(path))
                {
                    path = "/" + path;
                }

                string url = $"{this.Request.Scheme}://{this.Request.Host}{path}{this.Request.QueryString}";

                return url;
            }
        }

        /// <inheritdoc />
        public string UriWithoutQueryString
        {
            get
            {
                return $"{this.Request.Scheme}://{this.Request.Host}{this.Request.Path}";
            }
        }

        /// <inheritdoc />
        public string Method
        {
            get
            {
                return this.Request.Method;
            }
        }

        /// <inheritdoc />
        public IDictionary<string, StringValues> Headers
        {
            get
            {
                return this.Request.Headers;
            }
        }
    }
}