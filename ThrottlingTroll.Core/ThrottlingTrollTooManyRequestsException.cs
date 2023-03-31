using System;

namespace ThrottlingTroll
{
    /// <summary>
    /// A dedicated exception to propagate 429 status codes from Egress to Ingress
    /// </summary>
    public class ThrottlingTrollTooManyRequestsException : Exception
    {
        public string RetryAfterHeaderValue { get; set; }
    }
}