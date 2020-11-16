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
        public const string ForceUpdateParameterName = "force";
        public const string MonitoringTestParameterName = "monitoringtest";
        
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
                
                logger.LogInformation("Certificate {certificateName} found, subject: {subject}, expiration date: {expirationData}", 
                    name, cert.Policy.Subject, cert.Properties.ExpiresOn);
                
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
                
                logger.LogInformation($"Function return code: {resultCode}");

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
        /// Параметр monitoringtest=true приводит к записи в лог тестового исключения (Exception telemetry) и возврата
        /// кода ошибки 400
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
                //Throw exception if monitoringtest=true
                ThrowTestMonitoringException(req);
                
                var newCert = await certificateService.UpdateCertificateAsync(name, IsForceParam(req));

                if (newCert == null)
                {
                    logger.LogInformation("Certificate is not expired, no update needed!");
                    return new NoContentResult();
                }
                
                logger.LogInformation("Certificate {certificateName} updated, subject: {subject}, expiration date: {expirationData}", 
                    name, newCert.Policy.Subject, newCert.Properties.ExpiresOn);

                return new ObjectResult(new CertificateDto(name, newCert)) 
                    {StatusCode = (int) HttpStatusCode.OK};
            }
            catch (Exception e)
            {
                return ProcessException(e);
            }
        }

        protected void ThrowTestMonitoringException(HttpRequest req)
        {
            if (IsMonitoringTestParam(req))
            {
                throw new RequestProcessingException("Monitoring test exception!", HttpStatusCode.BadRequest);
            }
        }

        protected IActionResult ProcessException(Exception e)
        {
            ObjectResult ret;

            string msg = e.GetType() + ": " + e.Message;
            
            if (e is RequestProcessingException reqExc)
            {
                ret = new ObjectResult(msg) {StatusCode = (int) reqExc.StatusCode};
            }
            else if (e is ArgumentException)
            {
                ret = new ObjectResult(msg) 
                    {StatusCode = (int) HttpStatusCode.BadRequest};
            }
            else
            {
                ret = new  ObjectResult(msg)
                    {StatusCode = (int) HttpStatusCode.InternalServerError};
            }
            
            logger.LogError(e, $"Error in function (return code: {ret.StatusCode}): {msg}");

            return ret;
        }

        protected bool IsForceParam(HttpRequest req) => IsBoolParam(req, ForceUpdateParameterName);
        protected bool IsMonitoringTestParam(HttpRequest req) => IsBoolParam(req, MonitoringTestParameterName);

        
        protected bool IsBoolParam(HttpRequest req, string paramName)
        {
            bool ret = false;

            if (req.Query.ContainsKey(paramName) && !bool.TryParse(req.Query[paramName], out ret))
                throw new ArgumentException($"Invalid value of the query string \"{paramName}\" parameter!");

            return ret;
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