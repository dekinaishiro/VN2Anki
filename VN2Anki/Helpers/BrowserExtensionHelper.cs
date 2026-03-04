using System;
using System.IO;
using System.Linq;

namespace VN2Anki.Helpers
{
    public static class BrowserExtensionHelper
    {
        public static string[] GetDefaultExtensionBasePaths()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return new string[]
            {
                Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Extensions"),
                Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Extensions")
            };
        }

        public static string GetYomitanLatestVersionPath()
        {
            string yomitanId = "likgccmbimhjbgkjambclfkhldnlhbnn";
            foreach (var basePath in GetDefaultExtensionBasePaths())
            {
                string extPath = Path.Combine(basePath, yomitanId);
                if (Directory.Exists(extPath))
                {
                    var versionDirs = Directory.GetDirectories(extPath);
                    if (versionDirs.Length > 0)
                    {
                        return versionDirs.OrderByDescending(d => d).First();
                    }
                }
            }
            return null;
        }
    }
}
