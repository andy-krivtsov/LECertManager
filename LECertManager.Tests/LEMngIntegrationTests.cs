using System;
using System.Threading.Tasks;
using LECertManager.Configuration;
using LECertManager.Services;
using LECertManager.Tests.Helpers;
using Microsoft.Extensions.Configuration;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Console;
using Certes;
using Certes.Acme;
using Org.BouncyCastle.Asn1.X509;

namespace LECertManager.Tests
{
    // ReSharper disable once InconsistentNaming
    public class LEMngIntegrationTests
    {
        public ServiceProvider Services { get; set; }
        public AppSettings Settings { get; set; }
        
        public LEMngIntegrationTests()
        {
            this.Services = DiHelper.GetServiceProvider();
            Settings = Services.GetService<IOptions<AppSettings>>().Value;
            
            EnvHelper.SetAzureAccessEnvironment();
        }
        
        [Fact]
        public async Task RequestCertificate_Success()
        {
            AcmeService acmeService = Services.GetService<AcmeService>();

            var cetInfo = Settings.Certificates[0]; 
            byte[] certPfx = await acmeService.RequestCertificateAsync(cetInfo);
        }
        
         
        [Fact]
        public async Task UploadCertificateToKeyVault_Success()
        {
            KeyVaultService keyVaultService = Services.GetService<KeyVaultService>();

            var certPfx = CertsHelper.GetTestCertificatePfx();
            var cetInfo = Settings.Certificates[0];

            await keyVaultService.UploadCertificateAsync(cetInfo.KvCertName,
                cetInfo.KeyVault.Uri, certPfx, cetInfo.PfxPassword);
        }

        [Fact]
        public async Task RequestAndUploadCertificate_Success()
        {
            AcmeService acmeService = Services.GetService<AcmeService>();
            KeyVaultService keyVaultService = Services.GetService<KeyVaultService>();

            var cetInfo = Settings.Certificates[0]; 
            byte[] certPfx = await acmeService.RequestCertificateAsync(cetInfo);
            
            await keyVaultService.UploadCertificateAsync(cetInfo.KvCertName,
                cetInfo.KeyVault.Uri, certPfx, cetInfo.PfxPassword);
        }
        
        [Fact]
        public async Task FileKeyCache_Success()
        {
            var services = DiHelper.GetServiceProviderKeyCache<AcmeKeyFileCache>();

            var key = KeyFactory.NewKey(KeyAlgorithm.ES256);
            var cacheProvider = services.GetService<IAcmeKeyCache>();

            await cacheProvider.SaveAccountKey(key);

            var key2 = await cacheProvider.GetAccountKey();
            
            Assert.Equal(key.ToPem(), key2.ToPem());
        }
        
        [Fact]
        public async Task KeyVaultKeyCache_Success()
        {
            var services = DiHelper.GetServiceProviderKeyCache<AcmeKeyVaultCache>();

            var key = KeyFactory.NewKey(KeyAlgorithm.ES256);
            var cacheProvider = services.GetService<IAcmeKeyCache>();

            await cacheProvider.SaveAccountKey(key);

            var key2 = await cacheProvider.GetAccountKey();
            
            Assert.Equal(key.ToPem(), key2.ToPem());
        }
    }
}