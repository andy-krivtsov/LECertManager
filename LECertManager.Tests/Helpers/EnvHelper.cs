using System;

namespace LECertManager.Tests.Helpers
{
    public static class EnvHelper
    {
        public static void SetAzureAccessEnvironment()
        {
            Environment.SetEnvironmentVariable("AZURE_TENANT_ID", "a23826f6-c6a1-4088-ac87-47eee4634140");
            Environment.SetEnvironmentVariable("AZURE_CLIENT_SECRET", "0ioH5~Rmzu~j8B.t1_BmY5p6Nf8_4ho9q9");
            Environment.SetEnvironmentVariable("AZURE_CLIENT_ID", "2c6a97d9-2d77-4772-a8b2-cca64c996239");
        }
        
    }
}