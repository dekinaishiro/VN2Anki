using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using VN2Anki.Services;
using CommunityToolkit.Mvvm.Messaging;
using VN2Anki.Messages;

namespace VN2Anki
{
    public partial class OverlayWindow : Window, IRecipient<OverlayConfigUpdatedMessage>, IRecipient<SlotCapturedMessage>, IRecipient<BrowserExtensionUpdatedMessage>
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

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        private const uint WM_LBUTTONDOWN = 0x0201;
        private const uint WM_LBUTTONUP = 0x0202;
        private const uint WM_RBUTTONDOWN = 0x0204;
        private const uint WM_RBUTTONUP = 0x0205;

        private static IntPtr MakeLParam(int loWord, int hiWord)
        {
            return (IntPtr)((hiWord << 16) | (loWord & 0xFFFF));
        }

        private readonly IConfigurationService _configService;
        private readonly ITextHook _textHook;
        private readonly VN2Anki.Services.Interfaces.IWindowService _windowService;

        private IntPtr _webViewRenderHostHandle = IntPtr.Zero;
        private DispatcherTimer _holdTimer;
        private SUBCLASSPROC _webViewSubclassProc;

        private bool _isTextAtTop = false;
        private bool _isTransparent = true;
        private bool _isPassThroughToggled = false;
        private bool _isHoldActive = false;
        private bool _isMouseOverHeader = false; 
        private int _modifierKeyVk = 0xA2;

        public OverlayWindow(IConfigurationService configService, ITextHook textHook, VN2Anki.Services.Interfaces.IWindowService windowService)
        {
            InitializeComponent();
            _configService = configService;
            _textHook = textHook;
            _windowService = windowService;

            _webViewSubclassProc = new SUBCLASSPROC(WebViewSubclassProc);

            this.Loaded += OverlayWindow_Loaded;
            this.Closed += OverlayWindow_Closed;
            this.LocationChanged += OverlayWindow_LocationOrSizeChanged;
            this.SizeChanged += OverlayWindow_LocationOrSizeChanged;

            var conf = _configService.CurrentConfig.Overlay;
            _isTextAtTop = conf.IsTextAtTop;
            _isTransparent = conf.IsTransparent;
            _isPassThroughToggled = conf.IsPassThrough;

            DetermineModifierKey();
            CreateDynamicHtml();
            InitializeWebViewAsync();
            SetupHoldTimer();

            WeakReferenceMessenger.Default.RegisterAll(this); 
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
                <script>
                    document.addEventListener('mousedown', (e) => {{
                        let textBox = document.getElementById('text-box');
                        let insideTextBox = false;
                        if (textBox) {{
                            let r = textBox.getBoundingClientRect();
                            insideTextBox = e.clientX >= r.left && e.clientX <= r.right &&
                                            e.clientY >= r.top  && e.clientY <= r.bottom;
                        }}
                
                        if (!insideTextBox) {{
                            if (window.chrome && window.chrome.webview) {{
                                window.chrome.webview.postMessage(JSON.stringify({{
                                    forwardClick: true,
                                    x: e.screenX,
                                    y: e.screenY,
                                    button: e.button
                                }}));
                            }}
                            e.preventDefault();
                            e.stopPropagation();
                        }}
                    }});
                </script>
            </body>
            </html>";

            File.WriteAllText(htmlPath, htmlBase);
        }

        private async void InitializeWebViewAsync()
        {
            var options = new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = true };
            string userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VN2Anki", "WebView2Data");
            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
            await webView.EnsureCoreWebView2Async(environment);

            LoadExtensions();

            webView.WebMessageReceived += (s, e) =>
            {
                try
                {
                    string json = e.TryGetWebMessageAsString();
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        var root = doc.RootElement;
                        if (root.TryGetProperty("forwardClick", out JsonElement fwd) && fwd.GetBoolean())
                        {
                            int sx = (int)root.GetProperty("x").GetDouble();
                            int sy = (int)root.GetProperty("y").GetDouble();
                            int btn = (int)root.GetProperty("button").GetDouble();

                            POINT p = new POINT { X = sx, Y = sy };

                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                var handle = new WindowInteropHelper(this).Handle;
                                int style = GetWindowLong(handle, GWL_EXSTYLE);
                                SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_TRANSPARENT);
                                
                                IntPtr target = WindowFromPoint(p);
                                
                                SetWindowLong(handle, GWL_EXSTYLE, style);

                                if (target != IntPtr.Zero && target != handle)
                                {
                                    uint downMsg = btn == 2 ? WM_RBUTTONDOWN : WM_LBUTTONDOWN;
                                    uint upMsg   = btn == 2 ? WM_RBUTTONUP   : WM_LBUTTONUP;
                                    
                                    SetForegroundWindow(target);
                                    
                                    POINT clientP = p;
                                    ScreenToClient(target, ref clientP);
                                    
                                    IntPtr lParam = MakeLParam(clientP.X, clientP.Y);
                                    PostMessage(target, downMsg, IntPtr.Zero, lParam);
                                    PostMessage(target, upMsg,   IntPtr.Zero, lParam);
                                }
                            });
                        }
                    }
                }
                catch { }
            };

            webView.CoreWebView2.SetVirtualHostNameToFolderMapping("vn.local", AppDomain.CurrentDomain.BaseDirectory, CoreWebView2HostResourceAccessKind.Allow);
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
            if (webView.CoreWebView2 == null) return;
            var profile = webView.CoreWebView2.Profile;

            var loadedExtensions = await profile.GetBrowserExtensionsAsync();
            var enabledPaths = _configService.CurrentConfig.Overlay.CustomExtensions;

            // Get names of extensions we WANT to have enabled
            var targetExtensions = new List<(string Path, string Name)>();
            foreach (var path in enabledPaths)
            {
                if (Directory.Exists(path))
                {
                    var info = VN2Anki.Helpers.BrowserExtensionHelper.GetExtensionsFromPath(path).FirstOrDefault();
                    if (info != null) targetExtensions.Add((path, info.Name));
                }
            }

            // 1. Remove extensions that are NOT in the target list
            foreach (var loadedExt in loadedExtensions)
            {
                if (!targetExtensions.Any(t => t.Name == loadedExt.Name))
                {
                    try { await loadedExt.RemoveAsync(); } catch { }
                }
            }

            // 2. Add extensions that are in the target list but NOT yet loaded
            foreach (var target in targetExtensions)
            {
                if (!loadedExtensions.Any(l => l.Name == target.Name))
                {
                    try { await profile.AddBrowserExtensionAsync(target.Path); } catch { }
                }
            }
        }

        private void OverlayWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var conf = _configService.CurrentConfig.Overlay;
            
            if (!double.IsInfinity(conf.Width) && !double.IsNaN(conf.Width) && conf.Width > 0)
                this.Width = conf.Width;
            
            if (!double.IsInfinity(conf.Height) && !double.IsNaN(conf.Height) && conf.Height > 0)
                this.Height = conf.Height;
                
            if (!double.IsNaN(conf.Top) && !double.IsInfinity(conf.Top) && !double.IsNaN(conf.Left) && !double.IsInfinity(conf.Left))
            {
                this.Top = conf.Top;
                this.Left = conf.Left;
            }

            //_textHook.OnTextCopied += HandleNewText;
            ApplyPassThroughState();
        }

        private void HandleNewText(string text, DateTime timestamp)
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (webView.CoreWebView2 == null) return;

                string safeText = text.Replace("\r\n", "<br>")
                                      .Replace("\n", "<br>")
                                      .Replace("\r", "<br>")
                                      .Replace("\\", "\\\\")
                                      .Replace("'", "\\'");

                string injectScript = $@"
                    var container = document.getElementById('text-box');
                    container.innerHTML = '';
                    var newSpan = document.createElement('span');
                    newSpan.className = 'vn-text-line';
                    newSpan.innerHTML = '{safeText}';
                    container.appendChild(newSpan);
                ";
                webView.CoreWebView2.ExecuteScriptAsync(injectScript);
            }, System.Windows.Threading.DispatcherPriority.Normal);
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

        private void BtnExtensions_Click(object sender, RoutedEventArgs e)
        {
            _windowService.OpenExtensionsManager();
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

        private double _lastNormalTop = double.NaN;
        private double _lastNormalLeft = double.NaN;
        private double _lastNormalWidth = double.NaN;
        private double _lastNormalHeight = double.NaN;

        private void OverlayWindow_LocationOrSizeChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Normal)
            {
                _lastNormalTop = this.Top;
                _lastNormalLeft = this.Left;
                _lastNormalWidth = this.Width;
                _lastNormalHeight = this.Height;
            }
        }

        private void OverlayWindow_Closed(object sender, EventArgs e)
        {
            var conf = _configService.CurrentConfig.Overlay;
            conf.IsTextAtTop = _isTextAtTop;
            conf.IsTransparent = _isTransparent;
            conf.IsPassThrough = _isPassThroughToggled;

            // save current window position and size properly accounting for Minimized state
            if (this.WindowState == WindowState.Normal)
            {
                conf.Width = this.Width;
                conf.Height = this.Height;
                conf.Top = this.Top;
                conf.Left = this.Left;
            }
            else if (!double.IsNaN(_lastNormalTop))
            {
                conf.Width = _lastNormalWidth;
                conf.Height = _lastNormalHeight;
                conf.Top = _lastNormalTop;
                conf.Left = _lastNormalLeft;
            }
            else if (this.RestoreBounds != Rect.Empty)
            {
                conf.Width = this.RestoreBounds.Width;
                conf.Height = this.RestoreBounds.Height;
                conf.Top = this.RestoreBounds.Top;
                conf.Left = this.RestoreBounds.Left;
            }

            _configService.Save();
            //_textHook.OnTextCopied -= HandleNewText;

            if (_webViewRenderHostHandle != IntPtr.Zero)
            {
                RemoveWindowSubclass(_webViewRenderHostHandle, _webViewSubclassProc, 0);
            }

            _holdTimer?.Stop();
            webView?.Dispose();

            // unregister from messages to prevent memory leaks
            WeakReferenceMessenger.Default.UnregisterAll(this);
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
                        color: {cssFontColor}; 
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

        public void Receive(SlotCapturedMessage message)
        {
            DebugLogger.Log($"[7-OVERLAY] Message arrived at Overlay. Calling HandleNewText | Text: {message.Value.Text}");
            HandleNewText(message.Value.Text, message.Value.Timestamp);
        }

        public void Receive(BrowserExtensionUpdatedMessage message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                LoadExtensions();
            });
        }
    }
}
