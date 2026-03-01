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
using CommunityToolkit.Mvvm.Messaging;
using VN2Anki.Messages;

namespace VN2Anki
{
    public partial class OverlayWindow : Window, IRecipient<OverlayConfigUpdatedMessage>
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [DllImport("comctl32.dll", SetLastError = true)]
        public static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, uint uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        public static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, uint uIdSubclass);

        [DllImport("comctl32.dll", SetLastError = true)]
        public static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        public delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData);

        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;

        private readonly IConfigurationService _configService;
        private readonly ITextHook _textHook;

        private IntPtr _webViewRenderHostHandle = IntPtr.Zero;
        private DispatcherTimer _holdTimer;
        private SUBCLASSPROC _webViewSubclassProc;

        private bool _isTextAtTop = false;
        private bool _isTransparent = true;
        private bool _isPassThroughToggled = false;
        private bool _isHoldActive = false;
        private bool _isMouseOverHeader = false; 
        private int _modifierKeyVk = 0xA2;

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

            WeakReferenceMessenger.Default.Register(this);
        }

        private void DetermineModifierKey()
        {
            string mod = _configService.CurrentConfig.Overlay.PassThroughModifier;
            _modifierKeyVk = mod switch
            {
                "Alt" => 0xA4,
                "Shift" => 0xA0,
                _ => 0xA2
            };
        }

        private void ApplyPositionState()
        {
            if (webView.CoreWebView2 != null)
            {
                string dir = _isTextAtTop ? "flex-start" : "flex-end";
                string margin = _isTextAtTop ? "margin-top: 0px;" : "margin-bottom: 0px;";
                webView.CoreWebView2.ExecuteScriptAsync($@"
            document.body.style.justifyContent = '{dir}';
            document.getElementById('text-box').style.cssText += '{margin}';
        ");
            }
        }

        private void CreateDynamicHtml()
        {
            var conf = _configService.CurrentConfig.Overlay;
            string cssBgColor = WpfHexToCss(conf.BgColor);
            string cssFontColor = WpfHexToCss(conf.FontColor);

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
                background-color: rgba(0, 0, 0, 0.0) !important;
                overflow: hidden;
                display: flex; flex-direction: column; 
                justify-content: flex-end;
            }}
            #text-box {{
                color: {cssFontColor}; 
                background-color: {cssBgColor};
                font-size: {conf.FontSize}px; 
                padding: 10px;
                font-family: 'Segoe UI', sans-serif;
                border-radius: 8px; margin: 15px;
                transition: background 0.3s ease, color 0.3s ease;
                text-align: center;
                box-shadow: none;
                text-shadow: none;
            }}
        </style>
    </head>
    <body>
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
            // ensures that the page is always reloaded fresh, preventing caching issues when the overlay is restarted multiple times during development
            webView.CoreWebView2.Navigate($"http://vn.local/overlay.html?t={DateTime.Now.Ticks}");

            webView.NavigationCompleted += (s, e) =>
            {
                InstallWebViewSubclass();

                ApplyDynamicStyles();

                ApplyPositionState();
                ApplyTransparencyState();
            };
        }

        private async void LoadExtensions()
        {
            var profile = webView.CoreWebView2.Profile;
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
                        try { await profile.AddBrowserExtensionAsync(latestVersion); break; } catch { }
                    }
                }
            }

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
            var conf = _configService.CurrentConfig.Overlay;
            this.Width = conf.Width;
            this.Height = conf.Height;
            if (!double.IsNaN(conf.Top) && !double.IsNaN(conf.Left))
            {
                this.Top = conf.Top;
                this.Left = conf.Left;
            }

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
            if (uMsg == WM_NCHITTEST)
            {
                bool finalPassThrough = _isPassThroughToggled ^ _isHoldActive;
                if (finalPassThrough && !_isMouseOverHeader) return (IntPtr)HTTRANSPARENT;
            }
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

                bool finalPassThrough = _isPassThroughToggled ^ _isHoldActive;
                if (finalPassThrough)
                {
                    GetCursorPos(out POINT p);

                    try
                    {
                        Point mouseRelative = this.PointFromScreen(new Point(p.X, p.Y));

                        bool isOverHeader = (mouseRelative.X >= 0 && mouseRelative.X <= this.ActualWidth &&
                                             mouseRelative.Y >= 0 && mouseRelative.Y <= 40);

                        if (isOverHeader != _isMouseOverHeader)
                        {
                            _isMouseOverHeader = isOverHeader;
                            ApplyWindowExStyle(); 
                        }
                    }
                    catch
                    {
                       
                    }
                }
            };
            _holdTimer.Start();
        }

        private void ApplyPassThroughState()
        {
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
                _isMouseOverHeader = false;
            }

            ApplyWindowExStyle();
        }

        private void ApplyWindowExStyle()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            bool finalPassThrough = _isPassThroughToggled ^ _isHoldActive;

            if (finalPassThrough && !_isMouseOverHeader)
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT);
            }
            else
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle & ~WS_EX_TRANSPARENT);
            }
        }

        private void UpdateBackground()
        {
            string hexColor = _configService.CurrentConfig.Overlay.OverlayBgColor;
            System.Windows.Media.Color customColor;
            try { customColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor); }
            catch { customColor = System.Windows.Media.Colors.Black; }

            this.Background = _isTransparent ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0)) : new SolidColorBrush(customColor);
        }

        private void ApplyTransparencyState()
        {
            UpdateBackground();

            if (webView.CoreWebView2 != null)
            {
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
                    var loadedExtensions = await settingsWebView.CoreWebView2.Profile.GetBrowserExtensionsAsync();

                    var yomitanExt = loadedExtensions.FirstOrDefault(ext =>
                        ext.Id == "likgccmbimhjbgkjambclfkhldnlhbnn" ||
                        (ext.Name != null && ext.Name.Contains("Yomitan")));

                    if (yomitanExt != null)
                    {
                        settingsWebView.CoreWebView2.Navigate($"chrome-extension://{yomitanExt.Id}/settings.html");
                    }
                    else
                    {
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

        private void BtnPosition_Click(object sender, RoutedEventArgs e)
        {
            _isTextAtTop = !_isTextAtTop;
            ApplyPositionState();
        }

        private void BtnTransparency_Click(object sender, RoutedEventArgs e)
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

            // save current window position and size
            conf.Width = this.Width;
            conf.Height = this.Height;
            conf.Top = this.Top;
            conf.Left = this.Left;

            _configService.Save();
            _textHook.OnTextCopied -= HandleNewText;

            if (_webViewRenderHostHandle != IntPtr.Zero)
            {
                RemoveWindowSubclass(_webViewRenderHostHandle, _webViewSubclassProc, 0);
            }

            _holdTimer?.Stop();
            webView?.Dispose();

            // unregister from messages to prevent memory leaks
            WeakReferenceMessenger.Default.Unregister<OverlayConfigUpdatedMessage>(this);
        }

        private string WpfHexToCss(string hexColor)
        {
            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor);
                return $"rgba({color.R}, {color.G}, {color.B}, {(color.A / 255.0).ToString(System.Globalization.CultureInfo.InvariantCulture)})";
            }
            catch
            {
                return "rgba(0,0,0,0)";
            }
        }

        private void ApplyDynamicStyles()
        {
            if (webView.CoreWebView2 == null) return;

            var conf = _configService.CurrentConfig.Overlay;
            string cssBgColor = WpfHexToCss(conf.BgColor);
            string cssFontColor = WpfHexToCss(conf.FontColor);

            string script = $@"
                var style = document.getElementById('dynamic-config-style');
                if (!style) {{
                    style = document.createElement('style');
                    style.id = 'dynamic-config-style';
                    document.head.appendChild(style);
                }}
                style.innerHTML = `
                    #text-box {{
                        color: {cssFontColor} !important; 
                        font-size: {conf.FontSize}px !important;
                        background-color: {cssBgColor} !important;
                        text-shadow: none !important;
                    }}
                `;
            ";
            webView.CoreWebView2.ExecuteScriptAsync(script);
        }

        // this method will be called whenever the OverlayConfigUpdatedMessage is sent, allowing the overlay to update its appearance in real-time based on configuration changes
        public void Receive(OverlayConfigUpdatedMessage message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                UpdateBackground();
                ApplyDynamicStyles();
                ApplyTransparencyState();
            });
        }
    }
}