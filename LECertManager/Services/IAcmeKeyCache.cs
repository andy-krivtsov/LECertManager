using System;
using System.Threading.Tasks;
using Certes;

namespace LECertManager.Services
{
    public interface IAcmeKeyCache
    {
        public Task SaveAccountKey(IKey key, string serverAlias);
        public Task<IKey> GetAccountKey(string serverAlias);
    }
}