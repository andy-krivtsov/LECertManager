using System;
using System.Text;

namespace LECertManager.Configuration
{
    public class ConfigStorageAccess
    {
        private const string ConfigStorageUriVar = "ConfigStorageUri";
        private const string ConfigStorageSasVar = "ConfigStorageSas";
        private const string ConfigStorageKeyVar = "ConfigStorageKey";
        private const string ConfigStorageContainerVar = "ConfigStorageContainer";

        public Uri BlobUri { get; set; }
        public string SasToken { get; set; }
        public string Key { get; set; }
        public string ContainerName { get; set; }

        public string AccountName => (BlobUri != null) ? BlobUri.Host.Split(".")[0] : null;

        public string ConnectionString 
        {
            get {
                if (!Configured)
                    return null;

                string basePart = $"DefaultEndpointsProtocol=https;BlobEndpoint={BlobUri};";
                string keyPart = UsingSas ? $"SharedAccessSignature={SasToken}" : $"AccountName={AccountName};AccountKey={Key}";

                return basePart + keyPart;
            }
        }
        
        public bool Configured => BlobUri != null &&
                                  (!string.IsNullOrWhiteSpace(SasToken) || !string.IsNullOrWhiteSpace(Key)) &&
                                  !string.IsNullOrWhiteSpace(ContainerName);
        
        public bool UsingSas => !string.IsNullOrWhiteSpace(SasToken);

        public ConfigStorageAccess()
        {
            var confUri = Environment.GetEnvironmentVariable(ConfigStorageUriVar);
            BlobUri = string.IsNullOrWhiteSpace(confUri) ? null : new Uri(confUri);
            
            SasToken = Environment.GetEnvironmentVariable(ConfigStorageSasVar);
            Key = Environment.GetEnvironmentVariable(ConfigStorageKeyVar);
            ContainerName = Environment.GetEnvironmentVariable(ConfigStorageContainerVar);
        }

        
    }
}