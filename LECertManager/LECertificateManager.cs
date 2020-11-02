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

        /// <summary>
        /// Получить данные сертификата, управляемого приложением, из KeyVault
        /// URL для вызова: GET  https://function.application.uri/api/certificates/{name}
        /// Данные о сертификате и KeyVault берутся из конфигурации по ключу Name (параметр функции)
        /// </summary>
        /// <param name="req">HTTP-request, предоставляется Host'ом</param>
        /// <param name="name">Имя сертификата в конфигурации приложенрия</param>
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

        /// <summary>
        /// Проверить expiration-date сертификата , управляемого приложением, из KeyVault
        /// Если сертификат уже истек или сремя до истечения меньше 20 дней (задается в конфигурации)
        /// то возвращается 403 Forbidden, если не истек - 200 OK и DTO с данными сертификата
        /// 
        /// URL для вызова: GET  https://function.application.uri/api/certificates/{name}/status
        /// Данные о сертификате и KeyVault берутся из конфигурации по ключу Name (параметр функции)
        /// </summary>
        /// <param name="req">HTTP-request, предоставляется Host'ом</param>
        /// <param name="name">Имя сертификата в конфигурации приложенрия</param>
        /// <returns>DTO объет типа CertificateDto</returns>
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
        /// 
        /// По умолчанию функция не обновляет сертификат, если до его истечения осталось больше 20 дней (задается в конфигурации)
        /// Если сертификат не обновлен то возвращается 204 No Content
        /// 
        /// Для принудительного обновления  нужно указать парамтер force=true в Query запроса
        /// 
        /// </summary>
        /// <param name="req">HTTP-request, предоставляется Host'ом</param>
        /// <param name="name">Имя сертификата для обновления в конфигурации приложенрия</param>
        /// <returns></returns>
        [FunctionName("UpdateCertificate")]
        public async Task<IActionResult> UpdateCertificateAsync(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "certificates/{name}")]
            HttpRequest req, string name)
        {
            try
            {
                const string forceParam = "force";
                bool isForce = false;

                if (req.Query.ContainsKey(forceParam) && !bool.TryParse(req.Query[forceParam], out isForce))
                    throw new RequestProcessingException($"Invalid value of the query string \"force\" parameter!", HttpStatusCode.BadRequest);
                
                var newCert = await UpdateCertificateInternalAsync(name, isForce);

                if (newCert == null)
                    return new NoContentResult();

                return new ObjectResult(new CertificateDto(name, newCert)) {StatusCode = (int) HttpStatusCode.OK};
            }
            catch (RequestProcessingException e)
            {    
                return new ObjectResult(e.Message) {StatusCode = (int) e.StatusCode};
            }
        }
        
        /// <summary>
        /// Регулярная проверка всех сертификатов и обновление просроченных или близких к этому (за 20 дней, настраивается
        /// в конфигурации)
        /// Вызывается в 2:15 каждый день
        /// </summary>
        /// <param name="myTimer"></param>
        /// <returns></returns>
        [FunctionName("UpdateAllCertificates")]
        public async Task UpdateAllCertificates(
            [TimerTrigger("0 15 2 * * *")] TimerInfo myTimer)
        {
            foreach (var certInfo in settings.Certificates)
            {
                try
                {
                    logger.LogInformation("Interval Check & Update certificate {certificateName}", certInfo.Name);

                    await UpdateCertificateInternalAsync(certInfo.Name, false);
                }
                catch (Exception e)
                {
                    logger.LogError(e, $"Error during update certificate {certInfo.Name}");
                }
            }
            
            logger.LogInformation("Interval certificates update completed!");
        }

        /// <summary>
        /// Реализация обновления сертификата в KeyVault.
        /// Если force == false, то перед обновлением проверяется дата истечения существующего сертификата
        /// и сертификат обновляется только если он истек или истечет в течении 20 дней (параметр в кон-ии)  
        /// </summary>
        /// <param name="name">Имя сертификата в конфигурации</param>
        /// <param name="isForce">Если true - принудительно обновлять</param>
        /// <returns>Новый сертификат (объект KeyVaultCertificateWithPolicy) либо null, если не обновился</returns>
        /// <exception cref="RequestProcessingException">В случае проверяемых ошибок</exception>
        protected async Task<KeyVaultCertificateWithPolicy> UpdateCertificateInternalAsync(string name, bool isForce = false)
        {
            var certInfo = settings.Certificates.FirstOrDefault(x => x.Name.Equals(name));
            if (certInfo == null)
                throw new RequestProcessingException($"Certificate {name} not found in configuration!", HttpStatusCode.NotFound);
            
            //Проверить, нужно ли заменять существующий сертификат, если не установлен флаг Force 
            if (!isForce)
            {
                var oldCert = await keyVaultService.GetCertificateAsync(certInfo.KvCertName, certInfo.KeyVault.Uri);
                if (oldCert != null)
                {
                    logger.LogInformation("Certificate {certificateName} found, expiration date: {expirationData}", 
                        name, oldCert.Properties.ExpiresOn);

                    if(!IsExpired(oldCert.Properties.ExpiresOn,TimeSpan.FromDays(settings.RenewalBeforeExpireDays)))
                        return null;
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

            return newCert;
        }
        
        /// <summary>
        /// Реализация получения сертификата из KeyVault
        /// </summary>
        /// <param name="name">Имя сертификата в конфигурации</param>
        /// <returns></returns>
        /// <exception cref="RequestProcessingException">Выявленные ошибки в процессе</exception>
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

        protected static bool IsExpired(DateTimeOffset? expTime, TimeSpan buffer)
        {
            return expTime?.Subtract(DateTime.Now) <= buffer;
        }
    }
}