using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Azure.Identity;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using LECertManager.Configuration;
using Azure.ResourceManager.Dns;
using Azure.ResourceManager.Dns.Models;
using Certes.Pkcs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Authorization = Certes.Acme.Resource.Authorization;

namespace LECertManager.Services
{
    public class AcmeService
    {
        private readonly ILogger<AcmeService> logger;
        private readonly IAcmeKeyCache keyCache;
        private readonly IEnumerable<IAcmeChallengeHandler> challengeHandlers;
        private AppSettings settings;

        public AcmeService(ILogger<AcmeService> logger,
            IOptions<AppSettings> options, 
            IAcmeKeyCache keyCache,
            IEnumerable<IAcmeChallengeHandler> challengeHandlers
            )
        {
            this.logger = logger;
            this.keyCache = keyCache;
            this.challengeHandlers = challengeHandlers;
            this.settings = options.Value;
        }

        public async Task<AcmeContext> NewAcmeContextAsync(Uri acmeServerUri)
        {
            AcmeContext acmtCtx;
            
            var accountKey = await keyCache.GetAccountKey();
            if (accountKey != null)
            {
                acmtCtx = new AcmeContext(acmeServerUri, accountKey);
                await acmtCtx.Account();
                
                logger.LogInformation("Created ACME account from cached key, server: {serverUri}", acmeServerUri);
            }
            else
            {
                acmtCtx = new AcmeContext(acmeServerUri);
                await acmtCtx.NewAccount(settings.AcmeAccount.Email, true);
                await keyCache.SaveAccountKey(acmtCtx.AccountKey);
                
                logger.LogInformation("Created new ACME account, server: {serverUri}", acmeServerUri);
            }                
            
            return acmtCtx;
        }

        public async Task<byte[]> RequestCertificateAsync(CertificateInfo certInfo)
        {
            return await RequestCertificateAsync(certInfo, await NewAcmeContextAsync(certInfo.AcmeServerUri));
        }

        public async Task<byte[]> RequestCertificateAsync(CertificateInfo certInfo, AcmeContext acmeCtx)
        {
            logger.LogInformation("Requesting new LE certificate: {certificateRequest}", new
            {
                certInfo.Name,
                Domains = string.Join(",",certInfo.Domains)
            });
            
            //Создать новый запрос и получить список авторизаций
            var orderCtx = await acmeCtx.NewOrder(certInfo.Domains);

            //Выполнить операции по подтверждению владения доменами (Chanllenges)
            await ProcessOrderAuthorizations(orderCtx, acmeCtx, certInfo);

            //Подождать завершения проверок со стороны LE
            await WaitValidationAsync(orderCtx);
            
            //Получить сертификат в формате PFX и вернуть его
            return await GenerateCertificateAsync(certInfo, orderCtx);
        }

        protected async Task ProcessOrderAuthorizations(IOrderContext orderCtx, IAcmeContext acmeCtx, CertificateInfo certInfo)
        {
            // Пройти по всем авторизациям и создать необходимые DNS записи (DNS challenge)
            // или текстовые файлы (HTTP challenge)
            var authorizations = (await orderCtx.Authorizations()).ToList();
            foreach (IAuthorizationContext authCtx in authorizations)
            {
                Challenge challenge = null;
                
                foreach (var handler in challengeHandlers.OrderBy(x => x.Priority))
                {
                    challenge = await handler.DoChallengeAsync(authCtx, acmeCtx, certInfo);
                    if (challenge != null)
                        break;
                }
                
                //Если не нашлось ни одного handler'а для обработки авторизации, то выкинуть исключение это означает,
                //что мы не можем завершить подтвержденрие владения доменом
                if (challenge == null)
                    throw  new InvalidOperationException($"Can't complete authorizations for certificate {certInfo.Name}");
            }
        }

        protected async Task<byte[]> GenerateCertificateAsync(CertificateInfo certificateInfo, IOrderContext orderCtx)
        {
            //Создаем CSR-builder и добавляем туда все домены и CN
            var csrBuilder = new CertificationRequestBuilder();
            
            csrBuilder.AddName("CN", certificateInfo.CommonName);

            foreach (var domain in certificateInfo.Domains)
            {
                csrBuilder.SubjectAlternativeNames.Add(domain);    
            }
            
            //Отправляем запрос на завершение Order'а (генерацию сертификата)
            var order = await orderCtx.Finalize(csrBuilder.Generate());
            if(order.Status != OrderStatus.Valid)
                throw  new InvalidOperationException("Order finalization error!");

            //Получаем цепочку сертификатов и возвращаем ее виде PFX-файла
            var certChain = await orderCtx.Download();
            return certChain.ToPfx(csrBuilder.Key).Build(certificateInfo.CommonName, certificateInfo.PfxPassword);
        }

        protected async Task WaitValidationAsync(IOrderContext orderCtx)
        {
            var authorizations = (await orderCtx.Authorizations()).ToList();
            Authorization[] authzList = null;
            
            //Ждем пока все проверки будут закончены так или иначе (не останется статусов Pending) 
            while (true)
            {
                 authzList = await Task.WhenAll(authorizations.Select(x=> x.Resource()));

                 if (authzList.FirstOrDefault(x => x.Status == AuthorizationStatus.Pending) == null)
                     break;
                    
                 await Task.Delay(TimeSpan.FromSeconds(5));
            }

            //Если все проверки закончились успешно, то возвращаемся
            if (authzList.All(x => x.Status == AuthorizationStatus.Valid))
                return;

            //Если часть закончилась с ошибками, то собираем все ощибки в большую строку и кидаем исключение с ней
            var errors = authzList.Where(x => x.Status != AuthorizationStatus.Valid)
                .Select(x =>
                {
                    var errorsDetails = x.Challenges
                        .Where(c => c.Status == ChallengeStatus.Invalid)
                        .Select(c => c.Error?.Detail ?? "");

                    return x.Identifier.Value + ": " + string.Join(",", errorsDetails);
                });
            
            throw new InvalidOperationException($"Error in domain validation: {string.Join(";",errors)}");
        }
    }
}