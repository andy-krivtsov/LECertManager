using System;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.ResourceManager.Dns;
using Azure.ResourceManager.Dns.Models;
using LECertManager.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LECertManager.Services
{
    public class AzureDnsConnector : IDnsServiceConnector
    {
        private readonly ILogger<AzureDnsConnector> logger;
        public string Name => "AzureDns";

        public AzureDnsConnector(ILogger<AzureDnsConnector> logger)
        {
            this.logger = logger;
        }
        
        public async Task CreateDnsChallengeRecordAsync(string domainName, string value, DnsChallengeInfo dnsInfo)
        {
            var zoneInfo = dnsInfo.AzureDnsZone;
            if(zoneInfo == null)
                throw  new ArgumentNullException("dnsInfo.AzureDnsZone");
                
            string recordSetName = "_acme-challenge";

            //Если проверяемый домен находится ниже зоны, например test.contoso.com, а сама зона contoso.com, 
            //то название recortSet должно включать себя промежуточный дмомен: _acme-challenge.test
            if (!domainName.Equals(zoneInfo.Name))
            {
                int i = domainName.LastIndexOf("." + zoneInfo.Name);

                if (i == -1)
                    throw new ArgumentException($"Domain name {domainName} is not under the zone name {zoneInfo.Name}!");

                recordSetName = recordSetName + "." + domainName.Substring(0, i);
            }
            
            logger.LogInformation("Create DNS TXT record {name}: {value}", recordSetName + "." + zoneInfo.Name, value);
            
            var client = new DnsManagementClient(zoneInfo.SubscriptionId, new DefaultAzureCredential());
            
            await client.RecordSets.CreateOrUpdateAsync(
                zoneInfo.ResourceGroup,
                zoneInfo.Name,
                recordSetName,
                RecordType.TXT,
                new RecordSet()
                {    
                    TTL = 3600,
                    TxtRecords =
                    {
                        new TxtRecord()  { Value = {value } }
                    }
                }
            );
        }
    }
}