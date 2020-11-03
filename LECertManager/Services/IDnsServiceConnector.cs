using System.Threading.Tasks;
using LECertManager.Configuration;

namespace LECertManager.Services
{
    public interface IDnsServiceConnector
    {
        public string Name { get; }
        Task CreateDnsChallengeRecordAsync(string domainName, string value, DnsChallengeInfo dnsInfo, string orderId);
    }
}