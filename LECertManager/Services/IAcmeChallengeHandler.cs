using System.Threading.Tasks;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using LECertManager.Configuration;

namespace LECertManager.Services
{
    public interface IAcmeChallengeHandler
    {
        public int Priority { get; }

        public Task<Challenge> DoChallengeAsync(IAuthorizationContext authCtx, IAcmeContext acmeCtx, CertificateInfo certificateInfo);
    }
}