using System;
using System.Net;

namespace LECertManager.Exceptions
{
    public class RequestProcessingException : Exception
    {
        public HttpStatusCode StatusCode { get; set; }
        
        public RequestProcessingException() : base() { }
        public RequestProcessingException(string message) : base(message) { }

        public RequestProcessingException(string message, HttpStatusCode code) : base(message)
        {
            this.StatusCode = code;
        }
    }
}