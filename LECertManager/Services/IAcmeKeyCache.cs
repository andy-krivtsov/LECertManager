using System.Threading.Tasks;
using Certes;

namespace LECertManager.Services
{
    public interface IAcmeKeyCache
    {
        public Task SaveAccountKey(IKey key);
        public Task<IKey> GetAccountKey();
    }
}