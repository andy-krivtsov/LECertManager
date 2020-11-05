using System;
using System.IO;
using System.Threading.Tasks;
using Certes;
using LECertManager.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace LECertManager.Services
{
    public abstract class AcmeKeyCacheBase : IAcmeKeyCache
    {
        protected class KeyCacheContent
        {
            public string Key { get; set; }
            public DateTime Timestamp { get; set; }
            
        }
        
        protected ILogger<AcmeKeyCacheBase> logger;
        protected  AppSettings settings;

        public AcmeKeyCacheBase(ILogger<AcmeKeyCacheBase> logger, IOptions<AppSettings> options)
        {
            this.logger = logger;
            this.settings = options.Value;
        }

        public abstract Task WriteCacheData(string data);
        public abstract Task<string> ReadCacheData();

        public async Task SaveAccountKey(IKey key)
        {
            string pemKey = key?.ToPem();
            if(pemKey == null)
                throw  new ArgumentNullException(nameof(key));

            var oldContent = await ReadCacheData();
            if (!string.IsNullOrWhiteSpace(oldContent))
            {
                var cachedData = JsonConvert.DeserializeObject<KeyCacheContent>(oldContent);
                if (cachedData.Key.Equals(pemKey))
                    return; 
            }
                
            var content = JsonConvert.SerializeObject(
                new KeyCacheContent() { Key = pemKey, Timestamp =  DateTime.Now },
                Formatting.None);

            await WriteCacheData(content);
        }

        public async Task<IKey> GetAccountKey()
        {
            try
            {
                var content = await ReadCacheData();
                if (string.IsNullOrWhiteSpace(content))
                    return null;
                
                var cachedData = JsonConvert.DeserializeObject<KeyCacheContent>(content);

                return DateTime.Now.Subtract(cachedData.Timestamp).Hours <= settings.AcmeKeyCache.CacheTimeHours ? 
                    KeyFactory.FromPem(cachedData.Key) : null;
            }
            catch (Exception e)
            {
                logger.LogError(e,"Error during key cache loading!");
                return null;
            }
        }
    }
}