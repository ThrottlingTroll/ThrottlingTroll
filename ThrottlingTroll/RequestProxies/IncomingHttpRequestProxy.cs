using Microsoft.AspNetCore.Http;

namespace ThrottlingTroll
{
    /// <summary>
    /// Abstraction layer on top of <see cref="HttpRequest"/>
    /// </summary>
    public class IncomingHttpRequestProxy : IHttpRequestProxy
    {
        internal IncomingHttpRequestProxy(HttpRequest request)
        {
            this.Request = request;
        }

        /// <summary>
        /// Incoming <see cref="HttpRequest"/>
        /// </summary>
        public HttpRequest Request { get; private set; }

        /// <summary>
        /// Request URI
        /// </summary>
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

        /// <summary>
        /// Request URI without query string
        /// </summary>
        public string UriWithoutQueryString
        {
            get
            {
                return $"{this.Request.Scheme}://{this.Request.Host}{this.Request.Path}";
            }
        }


        /// <summary>
        /// Request HTTP method
        /// </summary>
        public string Method
        {
            get
            {
                return this.Request.Method;
            }
        }

        /// <summary>
        /// Request HTTP Headers
        /// </summary>
        public IHeaderDictionary Headers
        {
            get
            {
                return this.Request.Headers;
            }
        }
    }
}