using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Azure.Security.KeyVault.Certificates;
using LECertManager.Configuration;
using LECertManager.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LECertManager.Services
{
    public class CertificateService
    {
        private readonly ILogger<CertificateService> logger;
        private readonly AcmeService acmeService;
        private readonly KeyVaultService keyVaultService;
        private AppSettings settings;

        public CertificateService(ILogger<CertificateService> logger, 
            IOptions<AppSettings> options,
            AcmeService acmeService,
            KeyVaultService keyVaultService)
        {
            this.logger = logger;
            this.acmeService = acmeService;
            this.keyVaultService = keyVaultService;
            this.settings = options.Value;
        }
        
        /// <summary>
        /// Реализация получения сертификата из KeyVault
        /// </summary>
        /// <param name="name">Имя сертификата в конфигурации</param>
        /// <returns>Объект KeyVaultCertificateWithPolicy с информацией о сертификате или null, если сертификата нет в KeyVault</returns>
        public async Task<KeyVaultCertificateWithPolicy> GetCertificateAsync(string name)
        {
            var certInfo = GetCertificateInfo(name);

            //GetCertificateAsync() вернет null, только если сертификата с этим именем нет в KeyVault
            return await keyVaultService.GetCertificateAsync(certInfo.KvCertName, certInfo.KeyVault.Uri);
        }
        
        /// <summary>
        /// Реализация обновления сертификата в KeyVault.
        /// Если isForce == false, то перед обновлением проверяется дата истечения существующего сертификата
        /// и сертификат обновляется только если срок его жизнь истечет в течении 20 дней (параметр в кон-ии)  
        /// </summary>
        /// <param name="name">Имя сертификата в конфигурации</param>
        /// <param name="isForce">Если true - принудительно обновлять</param>
        /// <returns>Новый сертификат (объект KeyVaultCertificateWithPolicy) либо null, если обновление не понадобилось</returns>
        public async Task<KeyVaultCertificateWithPolicy> UpdateCertificateAsync(string name, bool isForce = false)
        {
            var certInfo = GetCertificateInfo(name);
            
            //Проверить, нужно ли заменять существующий сертификат, если не установлен флаг Force 
            if (!isForce)
            {
                var oldCert = await keyVaultService.GetCertificateAsync(certInfo.KvCertName, certInfo.KeyVault.Uri);
                if (oldCert != null)
                {
                    logger.LogInformation("Certificate {certificateName} found, expiration date: {expirationData}", 
                        name, oldCert.Properties.ExpiresOn);

                    if (!IsExpired(oldCert))
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

            //Получить данные о нем из KV обратно
            var newCert = await GetCertificateAsync(name);
            if(newCert == null)
                throw new InvalidOperationException("Can't get new certificate back from KeyVault!");

            return newCert;
        }

        /// <summary>
        /// Получить CertificateInfo из конфигурации по названию или выкинуть исключения
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public CertificateInfo GetCertificateInfo(string name)
        {
            if(string.IsNullOrWhiteSpace(name))
                throw new ArgumentNullException(nameof(name),"Certificate name can't be null or empty");
            
            var certInfo = settings.Certificates.FirstOrDefault(x => x.Name.Equals(name));
            if (certInfo == null)
                throw new ArgumentException($"Certificate {name} not found in the configuration!");

            return certInfo;
        }
        
        /// <summary>
        /// Проверка Expiration Date сертификата относительно текущей даты с запасом 20 дней
        /// (настраивается в конфигурации)
        /// </summary>
        public bool IsExpired(KeyVaultCertificateWithPolicy cert) =>
            IsExpired(cert, TimeSpan.FromDays(settings.RenewalBeforeExpireDays));
        
        public bool IsExpired(KeyVaultCertificateWithPolicy cert, TimeSpan buffer) =>
            IsExpired(cert.Properties.ExpiresOn, buffer);

        public static bool IsExpired(DateTimeOffset? expTime, TimeSpan buffer)
        {
            return expTime?.Subtract(DateTime.Now) <= buffer;
        }
    }
}