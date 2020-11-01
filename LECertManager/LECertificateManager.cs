using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Azure.Security.KeyVault.Certificates;
using LECertManager.Configuration;
using LECertManager.Exceptions;
using LECertManager.Models;
using LECertManager.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LECertManager
{
    // ReSharper disable once InconsistentNaming
    public class LECertificateManager
    {
        private readonly KeyVaultService keyVaultService;
        private readonly AcmeService acmeService;
        private readonly ILogger<LECertificateManager> logger;
        private readonly AppSettings settings;

        public LECertificateManager(KeyVaultService keyVaultService,
                AcmeService acmeService,
                ILogger<LECertificateManager> logger,
                IOptions<AppSettings> options
            )
        {
            this.keyVaultService = keyVaultService;
            this.acmeService = acmeService;
            this.logger = logger;
            this.settings = options.Value;
        }

        protected async Task<KeyVaultCertificateWithPolicy> GetCertificateInternal(string name)
        {
            if(string.IsNullOrWhiteSpace(name))
                throw new RequestProcessingException("Certificate name can't be empty!", HttpStatusCode.BadRequest);
            
            var certInfo = settings.Certificates.FirstOrDefault(x => x.Name == name);
            if (certInfo == null)
                throw new RequestProcessingException($"Certificate {name} not found in the configuration!", HttpStatusCode.NotFound);

            var cert = await keyVaultService.GetCertificateAsync(certInfo.KvCertName, certInfo.KeyVault.Uri);
            if (cert == null)
            {
                logger.LogError("Certificate {certificateName} found, but can't get it from the KeyVault!", name);
                
                throw new RequestProcessingException($"Can't get certificate from the KeyVault", HttpStatusCode.ServiceUnavailable);
            }

            return cert;
        }

        /// <summary>
        /// Получить данные сертификата, управляемого приложением, из KeyVault
        /// URL для вызова: GET  https://function.application.uri/api/certificates/{name}
        /// Данные о сертификате и KeyVault берутся из конфигурации по ключу Name (параметр функции)
        /// </summary>
        /// <param name="req">HTTP-request, предоставляется Host'ом</param>
        /// <param name="name">Имя сертификата для обновления в конфигурации приложенрия</param>
        /// <returns>DTO объет типа CertificateDto</returns>
        [FunctionName("GetCertificateInfo")]
        public async Task<IActionResult> GetCertificateInfoAsync(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "certificates/{name}")]
            HttpRequest req, string name)
        {
            try
            {
                var cert = await GetCertificateInternal(name);
                return new OkObjectResult(new CertificateDto(name, cert));
            }
            catch (RequestProcessingException e)
            {    
                return new ObjectResult(e.Message) {StatusCode = (int) e.StatusCode};
            }
        }

        [FunctionName("CheckCertificate")]
        public async Task<IActionResult> CheckCertificateAsync(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "certificates/{name}/status")]
            HttpRequest req, string name)
        {
            try
            {
                var cert = await GetCertificateInternal(name);
                logger.LogInformation("Certificate {certificateName} found, expiration date: {expirationData}", name, cert.Properties.ExpiresOn);

                var resultCode = HttpStatusCode.OK;
                
                if(IsExpired(cert.Properties.ExpiresOn,TimeSpan.Zero))
                {
                    logger.LogWarning("Certificate {certificateName} expired! Return false (403)!");
                    resultCode = HttpStatusCode.Forbidden;
                }
                
                return new ObjectResult(new CertificateDto(name, cert)) {StatusCode = (int)resultCode};
            }
            catch (RequestProcessingException e)
            {    
                return new ObjectResult(e.Message) {StatusCode = (int) e.StatusCode};
            }
        }
        
        /// <summary>
        /// Обновление сертификата в KeyVault из Lets Encrypt
        /// URL для вызова: POST  https://function.application.uri/api/certificates/{name}
        /// Данные о сертификате и KeyVault берутся из конфигурации по ключу Name (параметр функции)
        /// </summary>
        /// <param name="req">HTTP-request, предоставляется Host'ом</param>
        /// <param name="name">Имя сертификата для обновления в конфигурации приложенрия</param>
        /// <returns></returns>
        [FunctionName("UpdateCertificate")]
        public async Task<IActionResult> UpdateCertificateAsync(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "certificates/{name}")]
            HttpRequest req, string name)
        {
            var certInfo = settings.Certificates.FirstOrDefault(x => x.Name.Equals(name));
            if (certInfo == null)
                return new NotFoundObjectResult($"Certificate {name} not found in configuration!");

            const string forceParam = "force";
            bool isForce = false;

            if (req.Query.ContainsKey(forceParam) && !bool.TryParse(req.Query[forceParam], out isForce))
            {
                return new BadRequestObjectResult($"Invalid value of the query string \"force\" parameter!");
            }

            if (!isForce)
            {
                var oldCert = await keyVaultService.GetCertificateAsync(certInfo.KvCertName, certInfo.KeyVault.Uri);
                if (oldCert != null)
                {
                    logger.LogInformation("Certificate {certificateName} found, expiration date: {expirationData}", 
                        name, oldCert.Properties.ExpiresOn);
                    
                    if(!IsExpired(oldCert.Properties.ExpiresOn,TimeSpan.FromDays(settings.RenewalBeforeExpireDays)))
                    {
                        return new NoContentResult();           
                    }
                }
            }

            //Получить новый сертификат от LE
            var newCertPfx = await acmeService.RequestCertificateAsync(certInfo);

            //Загрузить его в KeyVault
            await keyVaultService.UploadCertificateAsync(certInfo.KvCertName, 
                certInfo.KeyVault.Uri, 
                newCertPfx, 
                certInfo.PfxPassword);

            //Получить его из KV обратно
            var newCert = await GetCertificateInternal(name);
                
            return new ObjectResult(new CertificateDto(name, newCert)) {StatusCode = (int)HttpStatusCode.OK};
        }

        protected static bool IsExpired(DateTimeOffset? expTime, TimeSpan buffer)
        {
            return expTime?.Subtract(DateTime.Now) <= buffer;
        }
    }
}