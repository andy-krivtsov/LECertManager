using System.IO;

namespace LECertManager.Tests.Helpers
{
    public static class CertsHelper
    {
        public const string PfxFileName = "test-import-cert.pfx";
        
        public static byte[] GetTestCertificatePfx()
        {
            var appRootDir  = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

            return File.ReadAllBytes(Path.Combine(appRootDir ?? "", PfxFileName));
        }
    }
}