using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
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
        private readonly CertificateService certificateService;
        private readonly ILogger<LECertificateManager> logger;
        private readonly AppSettings settings;

        public LECertificateManager(
                CertificateService certificateService,
                ILogger<LECertificateManager> logger,
                IOptions<AppSettings> options
            )
        {
            this.certificateService = certificateService;
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
                var cert = await certificateService.GetCertificateAsync(name);
                return new OkObjectResult(new CertificateDto(name, cert));
            }
            catch (Exception e)
            {
                return ProcessException(e);
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
                var cert = await certificateService.GetCertificateAsync(name);
                logger.LogInformation("Certificate {certificateName} found, expiration date: {expirationData}",
                    name, cert.Properties.ExpiresOn);

                var resultCode = HttpStatusCode.OK;

                if (certificateService.IsExpired(cert, TimeSpan.Zero))
                {
                    logger.LogWarning("Certificate {certificateName} expired! Return false (403)!");
                    resultCode = HttpStatusCode.Forbidden;
                }

                return new ObjectResult(new CertificateDto(name, cert)) {StatusCode = (int) resultCode};
            }
            catch (Exception e)
            {
                return ProcessException(e);
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
                var newCert = await certificateService.UpdateCertificateAsync(name, IsForceParam(req));

                if (newCert == null)
                    return new NoContentResult();

                return new ObjectResult(new CertificateDto(name, newCert)) 
                    {StatusCode = (int) HttpStatusCode.OK};
            }
            catch (Exception e)
            {
                return ProcessException(e);
            }
        }

        protected IActionResult ProcessException(Exception e)
        {
            ObjectResult ret;
            
            if (e is CertificateNotFoundException)
            {
                ret = new ObjectResult(e.Message) {StatusCode = (int) HttpStatusCode.NotFound};
            }else if (e is ArgumentException)
            {
                ret = new ObjectResult(e.GetType() + ": " + e.Message) 
                    {StatusCode = (int) HttpStatusCode.BadRequest};
            }
            else
            {
                ret = new  ObjectResult(e.GetType() + ": " + e.Message)
                    {StatusCode = (int) HttpStatusCode.InternalServerError};
            }

            return ret;
        }

        protected bool IsForceParam(HttpRequest req)
        {
            const string forceParam = "force";
            bool isForce = false;

            if (req.Query.ContainsKey(forceParam) && !bool.TryParse(req.Query[forceParam], out isForce))
                throw new ArgumentException($"Invalid value of the query string \"force\" parameter!");

            return isForce;
        }
        
        /// <summary>
        /// Регулярная проверка всех сертификатов и обновление просроченных или близких к этому (за 20 дней, настраивается
        /// в конфигурации). В конфигурации сертификата должно быть autoUpdate == true, для автоматического обновления.
        /// Вызывается в 01:00 каждый день
        /// </summary>
        /// <param name="myTimer"></param>
        /// <returns></returns>
        [FunctionName("UpdateAllCertificates")]
        public async Task UpdateAllCertificates(
            [TimerTrigger("0 0 1 * * *")] TimerInfo myTimer)
        {
            foreach (var certInfo in settings.Certificates.Where(x => x.AutoUpdate))
            {
                try
                {
                    logger.LogInformation("Interval Check & Update certificate {certificateName}", certInfo.Name);

                    await certificateService.UpdateCertificateAsync(certInfo.Name, false);
                }
                catch (Exception e)
                {
                    logger.LogError(e, $"Error during update certificate {certInfo.Name}");
                }
            }
            
            logger.LogInformation("Interval certificates update completed!");
        }
    }
}