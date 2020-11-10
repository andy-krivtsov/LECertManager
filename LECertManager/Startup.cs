using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using LECertManager.Configuration;
using LECertManager.Services;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Strathweb.AspNetCore.AzureBlobFileProvider;

[assembly: FunctionsStartup(typeof(LECertManager.Startup))]

namespace LECertManager
{
    public class Startup : FunctionsStartup
    {
        public const string ConfigStorageUriVar = "ConfigStorageUri";
        public const string ConfigStorageSasVar = "ConfigStorageSas";
        public const string ConfigStorageContainerVar = "ConfigStorageContainer";

        public const string ConfigFileName = "appsettings.json";
        public const string ConfigFileNameLocal = "appsettings.local.json";
            
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
                    .AddJsonFile(Path.Combine(context.ApplicationRootPath, ConfigFileNameLocal), false, false)
                    .AddEnvironmentVariables();
            }
            else
            {
                CheckConfigurationBlob(new Uri(confUri), confContainer, confSas).Wait();
                
                var blobOptions = new AzureBlobOptions
                    {BaseUri = new Uri(confUri), Token = confSas, DocumentContainer = confContainer};
                
                var azureBlobFileProvider = new AzureBlobFileProvider(blobOptions);
                
                builder.ConfigurationBuilder
                    .AddJsonFile(azureBlobFileProvider,ConfigFileName, false, false)
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

        protected async Task CheckConfigurationBlob(Uri blobUri, string containerName, string blobSas)
        {
            string conStr = $"BlobEndpoint={blobUri};SharedAccessSignature={blobSas}";
            var container = new BlobContainerClient(conStr, containerName);

            await container.CreateIfNotExistsAsync(PublicAccessType.None);

            var blobClient = container.GetBlobClient(ConfigFileName);

            if (await blobClient.ExistsAsync())
                return;
            
            string content = JsonConvert.SerializeObject(GetDefaultSettings(), new JsonSerializerSettings()
            {
                Formatting = Formatting.Indented,
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });

            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
            {
                
                await blobClient.UploadAsync(stream, new BlobHttpHeaders()
                {
                    ContentType = "application/json"
                });
            }
        }

        protected object GetDefaultSettings()
        {
            return new {
                AppSettings = new AppSettings()
                {
                    AcmeAccount = new AcmeAccountInfo()
                    {
                        Email = "admin@contoso.com"
                    }
                }
            };
        }
    }
}