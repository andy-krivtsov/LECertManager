using System;
using System.Collections.Generic;
using System.Linq;
using Azure.Security.KeyVault.Certificates;

namespace LECertManager.Models
{
    public class CertificateDto
    {
        public string Name { get; set; }
        public string KeyVaultName { get; set; }
        public string Version { get; set; }
        public string Thumbprint { get; set; }
        public Uri Id { get; set; }
        public string Subject { get; set; }
        public IEnumerable<string> SubjectAlternativeNames { get; set; }
        
        public string IssuerName { get; set; }
        public int? KeySize { get; set; }
        public IEnumerable<string> KeyUsage { get; set; } 
        public DateTimeOffset? Created { get; set; }
        public DateTimeOffset? Expires { get; set; }
        public DateTimeOffset? NotBefore { get; set; }
        public DateTimeOffset? Updated { get; set; }
        public bool? Enabled { get; set; }

        public CertificateDto(){}

        public CertificateDto(string name)
        {
            this.Name = name;
        }
        
        public CertificateDto(string name, KeyVaultCertificateWithPolicy cert)
        {
            this.Name = name;
            KeyVaultName = cert.Name;
            Version = cert.Properties.Version;
            Thumbprint = BitConverter.ToString(cert.Properties.X509Thumbprint).Replace("-","");
            Id = cert.Id;
            Subject = cert.Policy.Subject;
            SubjectAlternativeNames = cert.Policy?.SubjectAlternativeNames?.DnsNames;
            KeySize = cert.Policy.KeySize;
            KeyUsage = cert.Policy.KeyUsage.Select(x => x.ToString()).ToArray();
            Created = cert.Properties.CreatedOn;
            Expires = cert.Properties.ExpiresOn;
            NotBefore = cert.Properties.NotBefore;
            Updated = cert.Properties.UpdatedOn;
            Enabled = cert.Properties.Enabled;
            IssuerName = cert.Policy.IssuerName;
        }
    }
}