using System.Net;

namespace LECertManager.Exceptions
{
    public class CertificateNotFoundException : RequestProcessingException
    {
        public CertificateNotFoundException() :
            base("Certificate not found", HttpStatusCode.NotFound) { }
        
        public CertificateNotFoundException(string certName) : 
            base($"Certificate '{certName}' not found", HttpStatusCode.NotFound) { }
    }
}