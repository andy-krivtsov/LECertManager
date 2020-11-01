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
    public class AcmeKeyFileCache : AcmeKeyCacheBase
    {
        public AcmeKeyFileCache(ILogger<AcmeKeyFileCache> logger, IOptions<AppSettings> options):
            base(logger, options)
        {
        }

        public override async Task WriteCacheData(string data)
        {
            var filePath = Environment.ExpandEnvironmentVariables(settings.AcmeKeyCache?.FilePath ?? "");
            if (filePath == "")
                return;
            
            logger.LogInformation("Try to save account key to file: {path}",filePath);

            Directory.CreateDirectory(Path.GetDirectoryName(filePath));

            await File.WriteAllTextAsync(filePath,data);
        }

        public override async Task<string> ReadCacheData()
        {
            var filePath = Environment.ExpandEnvironmentVariables(settings.AcmeKeyCache?.FilePath ?? "");
            if (filePath == "" || !File.Exists(filePath))
                return null;

            logger.LogInformation("Try to read account key from file: {path}",filePath);
            
            return await File.ReadAllTextAsync(filePath);
        }
    }
}