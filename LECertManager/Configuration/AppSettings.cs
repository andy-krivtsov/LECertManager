using System;
using System.Collections.Generic;
using System.Linq;
using Certes.Acme;
// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable CollectionNeverUpdated.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable MemberCanBePrivate.Global

namespace LECertManager.Configuration
{
    public class KeyVaultInfo
    {
        public Uri Uri { get; set; }
        
    }

    public class AcmeAccountInfo
    {
        public string Email { get; set; }
    }

    public class DnsChallengeInfo
    {
        public string Provider { get; set; }
        public AzureDnsZoneInfo AzureDnsZone { get; set; }
    }

    public class AzureDnsZoneInfo
    {
        public string Name { get; set; }
        public string SubscriptionId { get; set; }
        public string ResourceGroup { get; set; }
    }
    
    public class HttpChallengeInfo
    {
        public string StorageUri { get; set; }
    }
    
    public class CertificateInfo
    {
        private string kvCertName;
        private string pfxPassword = "P@ssw0rd";
        public string Name { get; set; }

        public string CommonName => Domains.Count > 0 ? Domains[0] : null;

        public string PfxPassword
        {
            get => pfxPassword;
            set => pfxPassword = value;
        }

        public string KvCertName
        {
            get => !string.IsNullOrWhiteSpace(kvCertName) ? kvCertName : Name;
            set => kvCertName = value;
        }
        
        public string AcmeServer { get; set; }

        public Uri AcmeServerUri
        {
            get => AcmeServer.Equals("staging", StringComparison.CurrentCultureIgnoreCase)
                ? WellKnownServers.LetsEncryptStagingV2
                : WellKnownServers.LetsEncryptV2;
        }
        
        public DnsChallengeInfo DnsChallenge { get; set; }

        public HttpChallengeInfo HttpChallenge { get; set; }

        public List<string> Domains { get; } = new List<string>();
        public KeyVaultInfo KeyVault { get; set; }
    }

    public class AcmeKeyCacheInfo
    {
        public string FilePath { get; set; }
        
        public KeyVaultInfo KeyVault { get; set; }
        
        public string SecretName { get; set; }
    }
    
    public class AppSettings
    {
        public AcmeAccountInfo AcmeAccount { get; set; }
        public List<CertificateInfo> Certificates { get; } = new List<CertificateInfo>();
        
        public AcmeKeyCacheInfo AcmeKeyCache { get; set; }
        
        public int RenewalBeforeExpireDays { get; set; } = 20;
    }
}