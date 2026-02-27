using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using VN2Anki.Services;

namespace VN2Anki
{
    public partial class OverlayWindow : Window
    {
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);

        [DllImport("comctl32.dll", SetLastError = true)]
        public static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, uint uIdSubclass, IntPtr dwRefData);
        [DllImport("comctl32.dll", SetLastError = true)]
        public static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, uint uIdSubclass);
        [DllImport("comctl32.dll", SetLastError = true)]
        public static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
        public delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData);

        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;

        private readonly IConfigurationService _configService;
        private readonly ITextHook _textHook;

        private IntPtr _webViewRenderHostHandle = IntPtr.Zero;
        private DispatcherTimer _holdTimer;
        private SUBCLASSPROC _webViewSubclassProc;

        private bool _isTextAtTop = false;
        private bool _isTransparent = true;
        private bool _isPassThroughToggled = false;
        private bool _isHoldActive = false;
        private int _modifierKeyVk = 0xA2; // Default LCONTROL

        public OverlayWindow(IConfigurationService configService, ITextHook textHook)
        {
            InitializeComponent();
            _configService = configService;
            _textHook = textHook;

            _webViewSubclassProc = new SUBCLASSPROC(WebViewSubclassProc);

            this.Loaded += OverlayWindow_Loaded;
            this.Closed += OverlayWindow_Closed;

            var conf = _configService.CurrentConfig.Overlay;
            _isTextAtTop = conf.IsTextAtTop;
            _isTransparent = conf.IsTransparent;
            _isPassThroughToggled = conf.IsPassThrough;

            DetermineModifierKey();
            CreateDynamicHtml();
            InitializeWebViewAsync();
            SetupHoldTimer();
        }

        private void DetermineModifierKey()
        {
            string mod = _configService.CurrentConfig.Overlay.PassThroughModifier;
            _modifierKeyVk = mod switch
            {
                "Alt" => 0xA4,   // VK_LMENU (Left Alt explicitly)
                "Shift" => 0xA0, // VK_LSHIFT (Left Shift explicitly)
                _ => 0xA2        // VK_LCONTROL (Left Ctrl explicitly)
            };
        }

        private void ApplyPositionState()
        {
            if (webView.CoreWebView2 != null)
            {
                string dir = _isTextAtTop ? "flex-start" : "flex-end";
                string margin = _isTextAtTop ? "margin-top: 50px;" : "margin-bottom: 15px;";
                webView.CoreWebView2.ExecuteScriptAsync($@"
            document.body.style.justifyContent = '{dir}';
            document.getElementById('text-box').style.cssText += '{margin}';
        ");
            }
        }

        private void CreateDynamicHtml()
{
    var conf = _configService.CurrentConfig.Overlay;
    string appDir = AppDomain.CurrentDomain.BaseDirectory;
    string htmlPath = Path.Combine(appDir, "overlay.html");

    string htmlBase = $@"
        <!DOCTYPE html>
        <html>
        <head>
            <meta charset='utf-8'>
            <style>
                html, body {{
                    margin: 0; padding: 0; height: 100vh;
                    background-color: rgba(0, 0, 0, 0.01) !important;
                    overflow: hidden;
                    display: flex; flex-direction: column; 
                    justify-content: flex-end;
                }}
                #text-box {{
                    color: {conf.FontColor}; font-size: {conf.FontSize}px; padding: 25px;
                    font-family: 'Segoe UI', sans-serif;
                    border-radius: 8px; margin: 15px;
                    transition: background 0.3s ease;
                }}
                body.transp-on #text-box {{
                    background-color: rgba(0, 0, 0, 0.01) !important;
                    text-shadow: 2px 2px 0 #000, -1px -1px 0 #000, 1px -1px 0 #000, -1px 1px 0 #000, 1px 1px 0 #000;
                    box-shadow: none;
                }}
                body.transp-off #text-box {{
                    background: {conf.BgColor}; 
                    box-shadow: none; text-shadow: none;
                }}
            </style>
        </head>
        <body class='transp-on'>
            <div id='text-box'>Waiting for text...</div>
        </body>
        </html>";

    File.WriteAllText(htmlPath, htmlBase);
}

        private async void InitializeWebViewAsync()
        {
            var options = new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = true };
            var environment = await CoreWebView2Environment.CreateAsync(null, null, options);
            await webView.EnsureCoreWebView2Async(environment);

            LoadExtensions();

            webView.CoreWebView2.SetVirtualHostNameToFolderMapping("vn.local", AppDomain.CurrentDomain.BaseDirectory, CoreWebView2HostResourceAccessKind.Allow);
            webView.CoreWebView2.Navigate("http://vn.local/overlay.html");

            webView.NavigationCompleted += (s, e) =>
            {
                InstallWebViewSubclass();
                ApplyPositionState();    
                ApplyTransparencyState();
            };
        }

        private async void LoadExtensions()
        {
            var profile = webView.CoreWebView2.Profile;
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            // native yomitan support
         
            string yomitanId = "likgccmbimhjbgkjambclfkhldnlhbnn";
            string[] possiblePaths = {
                Path.Combine(localAppData, $@"Microsoft\Edge\User Data\Default\Extensions\{yomitanId}"),
                Path.Combine(localAppData, $@"Google\Chrome\User Data\Default\Extensions\{yomitanId}")
            };

            foreach (var path in possiblePaths)
            {
                if (Directory.Exists(path))
                {
                    var versionDirs = Directory.GetDirectories(path);
                    if (versionDirs.Length > 0)
                    {
                        string latestVersion = versionDirs.OrderByDescending(d => d).First();
                        try { await profile.AddBrowserExtensionAsync(latestVersion); break; } catch { }
                    }
                }
            }

            // custom extensions from config
            foreach (var extPath in _configService.CurrentConfig.Overlay.CustomExtensions)
            {
                if (Directory.Exists(extPath))
                {
                    try { await profile.AddBrowserExtensionAsync(extPath); } catch { }
                }
            }
        }

        private void OverlayWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _textHook.OnTextCopied += HandleNewText;
            ApplyPassThroughState();
        }

        private void HandleNewText(string text, DateTime timestamp)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (webView.CoreWebView2 == null) return;

                string safeText = text.Replace("\r\n", "<br>")
                                      .Replace("\n", "<br>")
                                      .Replace("\r", "<br>")
                                      .Replace("\\", "\\\\")
                                      .Replace("'", "\\'");

                webView.CoreWebView2.ExecuteScriptAsync($"document.getElementById('text-box').innerHTML = '{safeText}';");
            });
        }

        private void InstallWebViewSubclass()
        {
            IntPtr webViewHandle = webView.Handle;
            if (webViewHandle != IntPtr.Zero)
            {
                if (SetWindowSubclass(webViewHandle, _webViewSubclassProc, 0, IntPtr.Zero))
                {
                    _webViewRenderHostHandle = webViewHandle;
                }
            }
        }

        private IntPtr WebViewSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData)
        {
            bool finalPassThrough = _isPassThroughToggled ^ _isHoldActive;
            if (uMsg == WM_NCHITTEST && finalPassThrough) return (IntPtr)HTTRANSPARENT;
            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        private void SetupHoldTimer()
        {
            _holdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _holdTimer.Tick += (s, e) =>
            {
                bool isKeyDown = (GetAsyncKeyState(_modifierKeyVk) & 0x8000) != 0;
                if (isKeyDown != _isHoldActive)
                {
                    _isHoldActive = isKeyDown;
                    ApplyPassThroughState();
                }
            };
            _holdTimer.Start();
        }

        private void ApplyPassThroughState()
        {
            UpdateBackground();
            bool finalPassThrough = _isPassThroughToggled ^ _isHoldActive;

            if (finalPassThrough)
            {
                BtnPassThrough.Foreground = new SolidColorBrush(Colors.Red);
                BtnPassThrough.Content = "👻";
            }
            else
            {
                BtnPassThrough.Foreground = new SolidColorBrush(Colors.White);
                BtnPassThrough.Content = "🧱";
            }
        }

        private void ApplyTransparencyState()
        {
            UpdateBackground();

            if (webView.CoreWebView2 != null)
            {
                //avoids ArgumentException from WebView2 when trying to set a semi-transparent background
                webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;

                if (_isTransparent)
                {
                    webView.CoreWebView2.ExecuteScriptAsync("document.body.className = 'transp-on';");
                    BtnTransparencia.Foreground = new SolidColorBrush(System.Windows.Media.Colors.White);
                }
                else
                {
                    webView.CoreWebView2.ExecuteScriptAsync("document.body.className = 'transp-on transp-off';");
                    BtnTransparencia.Foreground = new SolidColorBrush(System.Windows.Media.Colors.Gray);
                }
            }
        }

        private void UpdateBackground()
        {
            bool finalPassThrough = _isPassThroughToggled ^ _isHoldActive;

            if (finalPassThrough)
            {
                this.Background = null;
            }
            else
            {
                // webview2 doesn't support semi-transparent backgrounds (throws ArgumentException), so we rely on the WPF window's background to create the transparency effect.
                string hexColor = _configService.CurrentConfig.Overlay.OverlayBgColor;
                System.Windows.Media.Color customColor;
                try { customColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor); }
                catch { customColor = System.Windows.Media.Colors.Black; }

                this.Background = _isTransparent
                    ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(5, 0, 0, 0))
                    : new SolidColorBrush(customColor);
            }
        }

        private void BtnYomitan_Click(object sender, RoutedEventArgs e)
        {
            var settingsWin = new Window
            {
                Title = "Yomitan Settings",
                Width = 900,
                Height = 700,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            var settingsWebView = new Microsoft.Web.WebView2.Wpf.WebView2();
            settingsWin.Content = settingsWebView;

            settingsWin.Loaded += async (ss, ee) =>
            {
                var options = new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = true };
                var environment = await CoreWebView2Environment.CreateAsync(null, null, options);
                await settingsWebView.EnsureCoreWebView2Async(environment);

                try
                {
                    // gets extensions loaded in memory
                    var loadedExtensions = await settingsWebView.CoreWebView2.Profile.GetBrowserExtensionsAsync();

                    // searches yomitan
                    var yomitanExt = loadedExtensions.FirstOrDefault(ext =>
                        ext.Id == "likgccmbimhjbgkjambclfkhldnlhbnn" ||
                        (ext.Name != null && ext.Name.Contains("Yomitan")));

                    if (yomitanExt != null)
                    {
                        settingsWebView.CoreWebView2.Navigate($"chrome-extension://{yomitanExt.Id}/settings.html");
                    }
                    else
                    {
                        // fallback
                        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                        string yomitanId = "likgccmbimhjbgkjambclfkhldnlhbnn";
                        string[] possiblePaths = {
                    Path.Combine(localAppData, $@"Microsoft\Edge\User Data\Default\Extensions\{yomitanId}"),
                    Path.Combine(localAppData, $@"Google\Chrome\User Data\Default\Extensions\{yomitanId}")
                };

                        foreach (var path in possiblePaths)
                        {
                            if (Directory.Exists(path))
                            {
                                var versionDirs = Directory.GetDirectories(path);
                                if (versionDirs.Length > 0)
                                {
                                    string latestVersion = versionDirs.OrderByDescending(d => d).First();
                                    var newExt = await settingsWebView.CoreWebView2.Profile.AddBrowserExtensionAsync(latestVersion);
                                    settingsWebView.CoreWebView2.Navigate($"chrome-extension://{newExt.Id}/settings.html");
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erro ao abrir configurações do Yomitan:\n{ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            settingsWin.Show();
        }

        private void BtnPosicao_Click(object sender, RoutedEventArgs e)
        {
            _isTextAtTop = !_isTextAtTop;
            ApplyPositionState();
        }

        private void BtnTransparencia_Click(object sender, RoutedEventArgs e)
        {
            _isTransparent = !_isTransparent;
            ApplyTransparencyState();
        }

        private void BtnPassThrough_Click(object sender, RoutedEventArgs e)
        {
            _isPassThroughToggled = !_isPassThroughToggled;
            ApplyPassThroughState();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => this.Close();

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void OverlayWindow_Closed(object sender, EventArgs e)
        {
            var conf = _configService.CurrentConfig.Overlay;
            conf.IsTextAtTop = _isTextAtTop;
            conf.IsTransparent = _isTransparent;
            conf.IsPassThrough = _isPassThroughToggled;
            _configService.Save();
            _textHook.OnTextCopied -= HandleNewText;
            
            if (_webViewRenderHostHandle != IntPtr.Zero)
            {
                RemoveWindowSubclass(_webViewRenderHostHandle, _webViewSubclassProc, 0);
            }
            
            _holdTimer?.Stop();
            
            // Kills the underlying Edge/WebView2 processes instantly
            webView?.Dispose(); 
        }
    }
}