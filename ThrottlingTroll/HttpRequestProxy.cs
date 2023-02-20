using System;
using System.Linq;
using System.Net.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace ThrottlingTroll
{
    /// <summary>
    /// Unifies <see cref="HttpRequest"/> (inbound requests) and <see cref="HttpRequestMessage"/> (outbound requests)
    /// </summary>
    public class HttpRequestProxy
    {
        internal HttpRequestProxy(HttpRequest incomingRequest)
        {
            this.IncomingRequest = incomingRequest;
        }

        internal HttpRequestProxy(HttpRequestMessage outgoingRequest)
        {
            this.OutgoingRequest = outgoingRequest;
        }

        /// <summary>
        /// Incoming <see cref="HttpRequest"/>
        /// </summary>
        public HttpRequest IncomingRequest { get; private set; }

        /// <summary>
        /// Outgoing <see cref="HttpRequestMessage"/>
        /// </summary>
        public HttpRequestMessage OutgoingRequest { get; private set; }

        /// <summary>
        /// Request URI
        /// </summary>
        public string Uri
        {
            get
            {
                if (this.IncomingRequest != null)
                {
                    string path = this.IncomingRequest.Path.ToString().Trim('/');
                    if (!string.IsNullOrEmpty(path))
                    {
                        path = "/" + path;
                    }

                    string url = $"{this.IncomingRequest.Scheme}://{this.IncomingRequest.Host}{path}{this.IncomingRequest.QueryString}";

                    return url;
                }

                if (this.OutgoingRequest != null)
                {
                    return this.OutgoingRequest.RequestUri?.ToString();
                }

                throw new InvalidOperationException($"Both IncomingRequest and OutgoingRequest are null. This should never happen.");
            }
        }

        /// <summary>
        /// Request URI without query string
        /// </summary>
        public string UriWithoutQueryString
        {
            get
            {
                if (this.IncomingRequest != null)
                {
                    return $"{this.IncomingRequest.Scheme}://{this.IncomingRequest.Host}{this.IncomingRequest.Path}";
                }

                if (this.OutgoingRequest != null)
                {
                    return $"{this.OutgoingRequest.RequestUri?.Scheme}://{this.OutgoingRequest.RequestUri?.Host}{this.OutgoingRequest.RequestUri?.AbsolutePath}";
                }

                throw new InvalidOperationException($"Both IncomingRequest and OutgoingRequest are null. This should never happen.");
            }
        }


        /// <summary>
        /// Request HTTP method
        /// </summary>
        public string Method
        {
            get
            {
                if (this.IncomingRequest != null)
                {
                    return this.IncomingRequest.Method;
                }

                if (this.OutgoingRequest != null)
                {
                    return this.OutgoingRequest.Method.Method;
                }

                throw new InvalidOperationException($"Both IncomingRequest and OutgoingRequest are null. This should never happen.");
            }
        }

        /// <summary>
        /// Request HTTP Headers
        /// </summary>
        public IHeaderDictionary Headers
        {
            get
            {
                if (this._headers == null)
                {
                    if (this.IncomingRequest != null)
                    {
                        this._headers = this.IncomingRequest.Headers;
                    }
                    else if (this.OutgoingRequest != null)
                    {
                        var headers = new HeaderDictionary();

                        foreach (var header in this.OutgoingRequest.Headers)
                        {
                            headers.Add(header.Key, new StringValues(header.Value.ToArray()));
                        }

                        this._headers = headers;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Both IncomingRequest and OutgoingRequest are null. This should never happen.");
                    }
                }

                return this._headers;
            }
        }

        private IHeaderDictionary _headers;
    }
}