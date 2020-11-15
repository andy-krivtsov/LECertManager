using System;
using System.Collections.Generic;
using System.Net;
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
        
        public async Task CreateDnsChallengeRecordAsync(string domainName, string value, DnsChallengeInfo dnsInfo,
            string orderId)
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

            var client = new DnsManagementClient(zoneInfo.SubscriptionId, new DefaultAzureCredential());

            var newRecordSet = new RecordSet()
            {
                TTL = 0,
                TxtRecords =
                {
                    new TxtRecord() {Value = {value}}
                },
                Metadata = {{"order", orderId}}
            };
            
            logger.LogInformation("Create DNS TXT record {name}: {value}", recordSetName + "." + zoneInfo.Name, value);
            
            //Попробовать сначала получить TXT RecordSet и проверить его метаданные.
            //Если это записи из того же Order, то добавить значение а не заменять его
            try
            {
                var oldRecSet = (await client.RecordSets.GetAsync(
                    zoneInfo.ResourceGroup,
                    zoneInfo.Name,
                    recordSetName,
                    RecordType.TXT
                )).Value;

                //Если в метаданных есть ID этого Order'а, то добавить существующие записи в список
                if (oldRecSet.Metadata.ContainsKey("order") &&
                    oldRecSet.Metadata["order"].Equals(orderId))
                {
                    logger.LogInformation("Found existing DNS TXT records for same order, add to list");

                    foreach (var txtRec in oldRecSet.TxtRecords)
                    {
                        newRecordSet.TxtRecords.Add(txtRec);    
                    }
                }
            }
            catch (Azure.RequestFailedException e) when (e.Status == (int) HttpStatusCode.NotFound)
            {}
            
            await client.RecordSets.CreateOrUpdateAsync(
                zoneInfo.ResourceGroup,
                zoneInfo.Name,
                recordSetName,
                RecordType.TXT,
                newRecordSet
            );

            //Wait 15 secs after any update DNS
            await Task.Delay(TimeSpan.FromSeconds(10));
        }
    }
}