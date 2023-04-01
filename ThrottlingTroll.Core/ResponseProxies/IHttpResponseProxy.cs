using System.Threading.Tasks;

namespace ThrottlingTroll
{
    /// <summary>
    /// Abstraction layer for all supported HTTP responses
    /// </summary>
    public interface IHttpResponseProxy
    {
        /// <summary>
        /// HTTP response status code
        /// </summary>
        public int StatusCode { get; set; }

        /// <summary>
        /// Sets value of a given HTTP response header
        /// </summary>
        public void SetHttpHeader(string headerName, string headerValue);

        /// <summary>
        /// Writes text to the response body.
        /// </summary>
        public Task WriteAsync(string text);
    }
}