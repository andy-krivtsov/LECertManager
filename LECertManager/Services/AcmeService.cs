using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
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
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.X509;
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
        
        protected Uri GetAcmeServerUri(string serverAlias)
        {
            if (serverAlias.Equals("staging", StringComparison.CurrentCultureIgnoreCase))
            {
                return WellKnownServers.LetsEncryptStagingV2;
            }
            else if (serverAlias.Equals("production", StringComparison.CurrentCultureIgnoreCase))
            {
                return WellKnownServers.LetsEncryptV2;
            }
            else
            {
                return null;
            }
        }

        public async Task<AcmeContext> NewAcmeContextAsync(string serverAlias)
        {
            Uri acmeServerUri = GetAcmeServerUri(serverAlias);
            
            AcmeContext acmeCtx;
            
            var accountKey = await keyCache.GetAccountKey(serverAlias);
            if (accountKey != null)
            {
                acmeCtx = new AcmeContext(acmeServerUri, accountKey);
                await acmeCtx.Account();
                
                logger.LogInformation("Created ACME account from cached key, server: {serverUri}", acmeServerUri);
            }
            else
            {
                acmeCtx = new AcmeContext(acmeServerUri);
                await acmeCtx.NewAccount(settings.AcmeAccount.Email, true);
                await keyCache.SaveAccountKey(acmeCtx.AccountKey, serverAlias);
                
                logger.LogInformation("Created new ACME account, server: {serverUri}", acmeServerUri);
            }                
            
            return acmeCtx;
        }

        public async Task<byte[]> RequestCertificateAsync(CertificateInfo certInfo)
        {
            return await RequestCertificateAsync(certInfo, await NewAcmeContextAsync(certInfo.AcmeServer));
        }

        public async Task<byte[]> RequestCertificateAsync(CertificateInfo certInfo, AcmeContext acmeCtx)
        {
            logger.LogInformation("Requesting new LE certificate: {certificateRequest}", new
            {
                certInfo.Name,
                Sever = certInfo.AcmeServer,
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
                    challenge = await handler.DoChallengeAsync(authCtx, acmeCtx, orderCtx, certInfo);
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
            LogNewCertificateInfo(certChain);
            
            return certChain.ToPfx(csrBuilder.Key).Build(certificateInfo.CommonName, certificateInfo.PfxPassword);
        }

        protected void LogNewCertificateInfo(CertificateChain certificateChain)
        {
            var certParser = new X509CertificateParser();
            var x509Cert = certParser.ReadCertificate(Encoding.UTF8.GetBytes(certificateChain.Certificate.ToPem()));
            
            logger.LogInformation("Received new certificate: {certData}", new
                {    
                    Subject = x509Cert.SubjectDN,
                    Expires = x509Cert.NotAfter,
                    Issuer = x509Cert.IssuerDN,
                    SubjAltNames = string.Join(",", x509Cert.GetSubjectAlternativeNames().Cast<ArrayList>().Select(x => x[1]))
                });
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