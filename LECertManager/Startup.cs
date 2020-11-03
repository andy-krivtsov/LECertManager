using System;
using System.IO;
using LECertManager.Configuration;
using LECertManager.Services;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strathweb.AspNetCore.AzureBlobFileProvider;

[assembly: FunctionsStartup(typeof(LECertManager.Startup))]

namespace LECertManager
{
    public class Startup : FunctionsStartup
    {
        public const string ConfigStorageUriVar = "ConfigStorageUri";
        public const string ConfigStorageSasVar = "ConfigStorageSas";
        public const string ConfigStorageContainerVar = "ConfigStorageContainer";
            
        /// <summary>
        /// Конфигурация загружается из переменных окружения
        /// И из Azure Storage если заданы переменные окружения
        ///   ConfigStorageUri
        ///   ConfigStorageSas
        ///   ConfigStorageContainer
        ///
        /// или из файла appsettings.local.json если переменные не заданы
        /// </summary>
        /// <param name="builder"></param>
        public override void ConfigureAppConfiguration(IFunctionsConfigurationBuilder builder)
        {
            FunctionsHostBuilderContext context = builder.GetContext();
            
            //Get enviroment variables for config file access
            var confUri = Environment.GetEnvironmentVariable(ConfigStorageUriVar);
            var confSas = Environment.GetEnvironmentVariable(ConfigStorageSasVar);
            var confContainer = Environment.GetEnvironmentVariable(ConfigStorageContainerVar);

            if (string.IsNullOrWhiteSpace(confUri) ||
                string.IsNullOrWhiteSpace(confSas) ||
                string.IsNullOrWhiteSpace(confContainer))
            {
                builder.ConfigurationBuilder
                    .AddJsonFile(Path.Combine(context.ApplicationRootPath, "appsettings.local.json"), false, false)
                    .AddEnvironmentVariables();
            }
            else
            {
                var blobOptions = new AzureBlobOptions
                    {BaseUri = new Uri(confUri), Token = confSas, DocumentContainer = confContainer};
                
                var azureBlobFileProvider = new AzureBlobFileProvider(blobOptions);
                
                builder.ConfigurationBuilder
                    .AddJsonFile(azureBlobFileProvider,"appsettings.json", false, false)
                    .AddEnvironmentVariables();
            }
        }

        public override void Configure(IFunctionsHostBuilder builder)
        {
            builder.Services.AddOptions<AppSettings>()
                .Configure<IConfiguration>((settings, configuration) =>
                {
                    configuration.GetSection("AppSettings").Bind(settings);
                });
            
            builder.Services.AddTransient<IAcmeKeyCache, AcmeKeyVaultCache>();
            builder.Services.AddTransient<KeyVaultService>();
            builder.Services.AddTransient<AcmeService>();
            builder.Services.AddTransient<IDnsServiceConnector, AzureDnsConnector>();
            builder.Services.AddTransient<IAcmeChallengeHandler, AcmeDnsChallengeHandler>();
            builder.Services.AddTransient<CertificateService>();
        }
    }
}