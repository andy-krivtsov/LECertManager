using System;
using System.Threading.Tasks;
using LECertManager.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LECertManager.Services
{
    public class AcmeKeyVaultCache : AcmeKeyCacheBase
    {
        private readonly KeyVaultService keyVaultService;

        public AcmeKeyVaultCache(ILogger<AcmeKeyFileCache> logger, IOptions<AppSettings> options, KeyVaultService keyVaultService):
            base(logger, options)
        {
            this.keyVaultService = keyVaultService;
        }

        public override async Task WriteCacheData(string data)
        {
            Uri uri = settings.AcmeKeyCache?.KeyVault?.Uri;
            string secretName = settings.AcmeKeyCache?.SecretName;

            if (uri == null || string.IsNullOrWhiteSpace(secretName))
                return;
            
            logger.LogInformation("Try to save account key to KeyVault: name={secretName}, uri={kvUri}",
                secretName, uri.ToString());

            await keyVaultService.SetSecretAsync(secretName, data, uri);
        }

        public override async Task<string> ReadCacheData()
        {
            Uri uri = settings.AcmeKeyCache?.KeyVault?.Uri;
            string secretName = settings.AcmeKeyCache?.SecretName;

            if (uri == null || string.IsNullOrWhiteSpace(secretName))
                return null;
            
            logger.LogInformation("Try to read account key from KeyVault: name={secretName}, uri={kvUri}",
                secretName, uri.ToString());
            
            return await keyVaultService.GetSecretAsync(secretName, uri);
        }
    }
}