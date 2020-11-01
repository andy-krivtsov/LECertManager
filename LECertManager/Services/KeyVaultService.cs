using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Secrets;
using LECertManager.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LECertManager.Services
{
    public class KeyVaultService
    {
        private readonly ILogger<KeyVaultService> logger;
        private readonly DefaultAzureCredential azureCredential;

        public KeyVaultService(ILogger<KeyVaultService> logger)
        {
            this.logger = logger;
            azureCredential = new DefaultAzureCredential();
        }

        public async Task<KeyVaultCertificateWithPolicy> GetCertificateAsync(string name, Uri keyVaultUri)
        {
            var client = new CertificateClient( keyVaultUri,  azureCredential);

            try
            {
                var cert = (await client.GetCertificateAsync(name)).Value;

                logger.LogInformation("Found certificate: {certInfo}", new {
                        cert.Properties.Name, cert.Properties.ExpiresOn,
                        Thumbprint = BitConverter.ToString(cert.Properties.X509Thumbprint).Replace("-", "")
                    });

                return cert;
            }
            catch (Azure.RequestFailedException e) when (e.Status == (int) HttpStatusCode.NotFound)
            {
                logger.LogWarning("Certificate {certName} not found!", name);
                return null;
            }
        }

        public async Task UploadCertificateAsync(string name, Uri keyVaultUri, byte[] pfxCertificate, string pfxPassword)
        {
            var client = new CertificateClient( keyVaultUri,  azureCredential);

            var importOptions = new ImportCertificateOptions(name, pfxCertificate)
            {
                Password = pfxPassword,
                Enabled = true
            };
            
            logger.LogInformation("Upload the certificate to KeyVault: KeyVault={keyVaultUri}, Name={certName}", keyVaultUri, name);

            await client.ImportCertificateAsync(importOptions);
        }

        public async Task<string> GetSecretAsync(string name, Uri keyVaultUri)
        {
            try
            {
                var client = new SecretClient( keyVaultUri,  azureCredential);

                return (await client.GetSecretAsync(name)).Value.Value;
            }
            catch (Azure.RequestFailedException e) when (e.Status == (int) HttpStatusCode.NotFound)
            {
                logger.LogWarning("Secret {secretName} not found!", name);
                return null;
            }      
        }
        
        public async Task SetSecretAsync(string name, string value,  Uri keyVaultUri)
        {
            var client = new SecretClient( keyVaultUri,  azureCredential);
            
            logger.LogInformation("Save secret to KeyVault: KeyVault={keyVaultUri}, Name={secretName}", keyVaultUri, name);
            
            await client.SetSecretAsync(name, value);
        }

    }
}