using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using LECertManager.Configuration;
using Microsoft.Extensions.Logging;

namespace LECertManager.Services
{
    public class AcmeDnsChallengeHandler : IAcmeChallengeHandler
    {
        private readonly ILogger<AcmeDnsChallengeHandler> logger;
        private readonly IEnumerable<IDnsServiceConnector> dnsConnectors;
        public int Priority => 1;

        public AcmeDnsChallengeHandler(ILogger<AcmeDnsChallengeHandler> logger, 
            IEnumerable<IDnsServiceConnector> dnsConnectors)
        {
            this.logger = logger;
            this.dnsConnectors = dnsConnectors;
        }
        
        public async Task<Challenge> DoChallengeAsync(IAuthorizationContext authCtx, IAcmeContext acmeCtx, CertificateInfo certificateInfo)
        {
            try
            {
                var dnsInfo = certificateInfo.DnsChallenge;
                if (dnsInfo == null)
                    return null;

                var domain = (await authCtx.Resource()).Identifier.Value;
                var challenge = await authCtx.Dns();

                if (challenge == null)
                    return null;

                logger.LogInformation("Process DNS Challenge for domain: {domain}", domain);

                var connector = dnsConnectors.FirstOrDefault(x => x.Name == dnsInfo.Provider);
                if(connector == null)
                    throw new InvalidOperationException($"Can't find DNS provider {dnsInfo.Provider}");
            
                await connector.CreateDnsChallengeRecordAsync(domain, acmeCtx.AccountKey.DnsTxt(challenge.Token), dnsInfo);

                return await challenge.Validate();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error in DNS authorization processing!");
                return null;
            }

        }
    }
}