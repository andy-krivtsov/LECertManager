using System;
using System.IO;
using System.Linq;
using LECertManager.Configuration;
using LECertManager.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Console;
using Strathweb.AspNetCore.AzureBlobFileProvider;


namespace LECertManager.Tests.Helpers
{
    public static class DiHelper
    {
        public static ServiceProvider GetServiceProvider()
        {
            //Create Configuration object from appsettings.local.json
            var appRootDir  = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            
            //Try to add file from cloud
            /*
            var blobOptions = new AzureBlobOptions
            {
                BaseUri = new Uri("https://lecertmgrdev.blob.core.windows.net"),
                Token = "?sv=2019-12-12&ss=b&srt=sco&sp=rl&se=2030-10-27T21:54:34Z&st=2020-10-27T13:54:34Z&spr=https&sig=zznCsilu7v8eeRsQIruww5Qi7Cgiem2zpCBfR%2Bg3ESo%3D",
                DocumentContainer = "le-cert-mgr-config"
            };
            
            var azureBlobFileProvider = new AzureBlobFileProvider(blobOptions);
            */
                        
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(appRootDir ?? "", "appsettings.local.json"), false)
                //.AddJsonFile(azureBlobFileProvider,"appsettings.local.json", false, false)
                .Build();
            
            //Create service collection
            ServiceCollection builder = new ServiceCollection();

            //Add configuration and logging
            builder.AddSingleton<IConfiguration>(configuration);
            builder.AddLogging(config => config.AddConsole());

            builder.AddOptions<AppSettings>()
                .Configure<IConfiguration>((settings, configuration) =>
                {
                    configuration.GetSection("AppSettings").Bind(settings);
                });

            //Add application services
            builder.AddTransient<IAcmeKeyCache, AcmeKeyFileCache>();
            builder.AddTransient<AcmeService>();
            builder.AddTransient<KeyVaultService>();
            builder.AddTransient<IDnsServiceConnector, AzureDnsConnector>();
            builder.AddTransient<IAcmeChallengeHandler, AcmeDnsChallengeHandler>();
            
            //Return ServiceProvicer
            return builder.BuildServiceProvider();
        }

        public static ServiceProvider GetServiceProviderKeyCache<T>() where T : AcmeKeyCacheBase
        {
            //Create Configuration object from appsettings.local.json
            var appRootDir  = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            var configuration = new ConfigurationBuilder().Build();
            
            //Create service collection
            ServiceCollection builder = new ServiceCollection();

            //Add configuration and logging
            builder.AddSingleton<IConfiguration>(configuration);
            builder.AddLogging(config => config.AddConsole());

            builder.AddOptions<AppSettings>()
                .Configure(settings =>
                {
                    settings.AcmeKeyCache = new AcmeKeyCacheInfo()
                    {
                        FilePath = "%APPDATA%\\LECertManager\\Debug\\key-cache.txt",
                        KeyVault = new KeyVaultInfo()
                        {
                            Uri = new Uri("https://kube-vault01.vault.azure.net/")
                        },
                        SecretName = "testKeyCache"
                    };
                });

            builder.AddTransient<KeyVaultService>();
            builder.AddTransient<IAcmeKeyCache, T>();
            //Return ServiceProvicer
            return builder.BuildServiceProvider();
        }

    }
}