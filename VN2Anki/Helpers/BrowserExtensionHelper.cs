using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace VN2Anki.Helpers
{
    public class BrowserExtensionInfo : INotifyPropertyChanged
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string Path { get; set; }
        public string IconPath { get; set; }

        private bool _isEnabled;
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public static class BrowserExtensionHelper
    {
        private static Microsoft.Web.WebView2.Core.CoreWebView2Environment _sharedEnvironment;
        private static readonly System.Threading.SemaphoreSlim _envLock = new(1, 1);

        public static async Task<Microsoft.Web.WebView2.Core.CoreWebView2Environment> GetSharedEnvironmentAsync()
        {
            await _envLock.WaitAsync();
            try
            {
                if (_sharedEnvironment == null)
                {
                    var options = new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = true };
                    string userDataFolder = GetAppDataFolder();
                    _sharedEnvironment = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
                }
                return _sharedEnvironment;
            }
            finally
            {
                _envLock.Release();
            }
        }

        public static string GetAppDataFolder()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VN2Anki", "WebView2Data");
        }

        public static Dictionary<string, string> GetBrowserPaths()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            return new Dictionary<string, string>
            {
                { "Chrome", Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Extensions") },
                { "Edge", Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Extensions") },
                { "Firefox", Path.Combine(appData, @"Mozilla\Firefox\Profiles") },
                { "Brave", Path.Combine(localAppData, @"BraveSoftware\Brave-Browser\User Data\Default\Extensions") }
                
            };
        }

        public static List<BrowserExtensionInfo> GetExtensionsFromPath(string basePath)
        {
            var extensions = new List<BrowserExtensionInfo>();
            if (string.IsNullOrEmpty(basePath) || !Directory.Exists(basePath)) return extensions;

            if (File.Exists(Path.Combine(basePath, "manifest.json")))
            {
                var info = ParseManifest(basePath);
                if (info != null) extensions.Add(info);
                return extensions;
            }

            foreach (var extDir in Directory.GetDirectories(basePath))
            {
                string id = Path.GetFileName(extDir);
                try 
                {
                    var versionDirs = Directory.GetDirectories(extDir);
                    if (versionDirs.Length > 0)
                    {
                        var latestVersionDir = versionDirs.OrderByDescending(d => d).First();
                        var info = ParseManifest(latestVersionDir);
                        if (info != null)
                        {
                            info.Id = id;
                            extensions.Add(info);
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error enumerating extensions: {ex.Message}"); }
            }

            return extensions;
        }

        private static BrowserExtensionInfo ParseManifest(string extensionPath)
        {
            try
            {
                string manifestPath = Path.Combine(extensionPath, "manifest.json");
                if (!File.Exists(manifestPath)) return null;

                string json = File.ReadAllText(manifestPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string name = root.TryGetProperty("name", out var n) ? n.GetString() : Path.GetFileName(extensionPath);
                if (name.StartsWith("__MSG_")) name = Path.GetFileName(Path.GetDirectoryName(extensionPath)); 

                string version = root.TryGetProperty("version", out var v) ? v.GetString() : "0.0";
                
                string iconPath = null;
                if (root.TryGetProperty("icons", out var icons))
                {
                    var iconSizes = icons.EnumerateObject().Select(p => p.Name).ToList();
                    string bestSize = iconSizes.OrderByDescending(s => {
                        int.TryParse(s, out int size);
                        return size;
                    }).FirstOrDefault();

                    if (bestSize != null)
                    {
                        string iconRelPath = icons.GetProperty(bestSize).GetString();
                        iconPath = Path.Combine(extensionPath, iconRelPath);
                    }
                }

                return new BrowserExtensionInfo
                {
                    Name = name,
                    Version = version,
                    Path = extensionPath,
                    IconPath = iconPath
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing extension manifest: {ex.Message}");
                return null;
            }
        }

        public static string GetYomitanLatestVersionPath()
        {
            string yomitanId = "likgccmbimhjbgkjambclfkhldnlhbnn";
            var browserPaths = GetBrowserPaths();
            
            foreach (var path in browserPaths.Values)
            {
                if (Directory.Exists(path))
                {
                    string extPath = Path.Combine(path, yomitanId);
                    if (Directory.Exists(extPath))
                    {
                        try 
                        {
                            var versionDirs = Directory.GetDirectories(extPath);
                            if (versionDirs.Length > 0)
                            {
                                return versionDirs.OrderByDescending(d => d).First();
                            }
                        }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error getting latest version path: {ex.Message}"); }
                    }
                }
            }
            return null;
        }
    }
}
