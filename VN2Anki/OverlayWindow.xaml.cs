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
using static VN2Anki.Services.Win32InteropService;

namespace VN2Anki
{
    public partial class OverlayWindow : Window, IRecipient<OverlayConfigUpdatedMessage>, IRecipient<SlotCapturedMessage>, IRecipient<BrowserExtensionUpdatedMessage>
    {
        private readonly IConfigurationService _configService;
        private readonly ITextHook _textHook;
        private readonly VN2Anki.Services.Interfaces.IWindowService _windowService;

        private IntPtr _webViewRenderHostHandle = IntPtr.Zero;
        private DispatcherTimer? _holdTimer;
        private SUBCLASSPROC _webViewSubclassProc;

        private bool _isTextAtTop = false;
        private bool _isTransparent = true;
        private bool _isPassThroughToggled = false;
        private bool _isHoldActive = false;
        private bool _isMouseOverHeader = false; 
        private int _modifierKeyVk = 0xA2;
        private bool _isLoaded = false;

        private double _lastNormalTop = double.NaN;
        private double _lastNormalLeft = double.NaN;
        private double _lastNormalWidth = double.NaN;
        private double _lastNormalHeight = double.NaN;

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
                var conf = _configService.CurrentConfig.Overlay;
                string dir = _isTextAtTop ? "flex-start" : "flex-end";

                // Aplica a margem vertical baseada na posição (Topo ou Fundo)
                string margin = _isTextAtTop
                    ? $"margin-top: {conf.VerticalMargin}px; margin-bottom: 0px;"
                    : $"margin-bottom: {conf.VerticalMargin}px; margin-top: 0px;";

                // Aplica o deslocamento horizontal (Displacement)
                string transform = $"transform: translateX({conf.HorizontalDisplacement}px);";

                webView.CoreWebView2.ExecuteScriptAsync($@"
            document.body.style.justifyContent = '{dir}';
            var tb = document.getElementById('text-box');
            if (tb) {{
                // Sobrescreve apenas a margem e o transform dinamicamente
                tb.style.cssText = '{margin} {transform}';
            }}
        ");
            }
        }

        private void CreateDynamicHtml()
        {
            var conf = _configService.CurrentConfig.Overlay;
            string cssBgColor = WpfHexToCss(conf.BgColor);
            string cssFontColor = WpfHexToCss(conf.FontColor);

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string vn2ankiFolder = Path.Combine(appData, "VN2Anki");
            if (!Directory.Exists(vn2ankiFolder)) Directory.CreateDirectory(vn2ankiFolder);
            string htmlPath = Path.Combine(vn2ankiFolder, "overlay.html");

            string htmlBase = $@"
            <!DOCTYPE html>
            <html>
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
            transition: background 0.2s ease, color 0.2s ease;
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
            try
            {
                var environment = await VN2Anki.Helpers.BrowserExtensionHelper.GetSharedEnvironmentAsync();
                await webView.EnsureCoreWebView2Async(environment);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao inicializar o WebView2: {ex.Message}\nVerifique se o WebView2 Runtime está instalado e se o aplicativo tem permissão de escrita na pasta AppData.", "Erro Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

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
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error handling click event in WebView: {ex.Message}"); }
            };

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string vn2ankiFolder = Path.Combine(appData, "VN2Anki");
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping("vn.local", vn2ankiFolder, CoreWebView2HostResourceAccessKind.Allow);
            webView.CoreWebView2.Navigate($"http://vn.local/overlay.html?t={DateTime.Now.Ticks}");

            webView.NavigationCompleted += (s, e) =>
            {
                InstallWebViewSubclass();

                ApplyDynamicStyles();

                ApplyPositionState();
                ApplyTransparencyState();
            };
        }

        private void ApplyDynamicStyles()
        {
            if (webView.CoreWebView2 == null) return;

            var conf = _configService.CurrentConfig.Overlay;
            string cssBgColor = WpfHexToCss(conf.BgColor);
            string cssFontColor = WpfHexToCss(conf.FontColor);
            string cssOutlineColor = WpfHexToCss(conf.OutlineColor);

            // Lógica do Modo Text Box vs Modo Solto
            string boxStyles = "";
            if (conf.UseTextBoxMode)
            {
                string align = string.IsNullOrEmpty(conf.TextVerticalAlignment) ? "center" : conf.TextVerticalAlignment;
                boxStyles = $@"
            min-height: {conf.TextBoxMinHeight}px;
            width: {conf.TextBoxWidthPercentage}vw;
            display: flex;
            flex-direction: column;
            justify-content: {align};
            align-items: center; /* Centraliza horizontalmente o texto dentro da caixa */
            box-sizing: border-box;
            margin-left: auto;
            margin-right: auto;
        ";
            }
            else
            {
                boxStyles = $@"
            width: auto;
            min-height: auto;
            display: inline-block;
            margin-left: auto;
            margin-right: auto;
        ";
            }

            // Lógica da Borda (Outline / Stroke) 100% Externa
            string textOutline = "";
            if (conf.OutlineThickness > 0)
            {
                int t = conf.OutlineThickness;
                string c = cssOutlineColor;

                // Cria uma matriz de text-shadow (círculo perfeito ao redor da letra)
                var shadowParts = new System.Collections.Generic.List<string>();
                for (int x = -t; x <= t; x++)
                {
                    for (int y = -t; y <= t; y++)
                    {
                        if (x == 0 && y == 0) continue;
                        shadowParts.Add($"{x}px {y}px 0px {c}");
                    }
                }
                // Adiciona um blur suave no final para arredondar as pontas dos kanjis
                shadowParts.Add($"0px 0px {t}px {c}");

                textOutline = "text-shadow: " + string.Join(", ", shadowParts) + " !important;\n";
                textOutline += "                -webkit-text-stroke: 0 !important;"; // Garante que o stroke nativo não atrapalhe
            }
            else
            {
                textOutline = "text-shadow: none !important; -webkit-text-stroke: 0 !important;";
            }

            // Injeção do CSS
            string script = $@"
        var style = document.getElementById('dynamic-config-style');
        if (!style) {{
            style = document.createElement('style');
            style.id = 'dynamic-config-style';
            document.head.appendChild(style);
        }}
        style.innerHTML = `
            html, body {{
                width: 100vw;
            }}
            #text-box {{
                color: {cssFontColor} !important; 
                font-family: '{conf.FontFamily}', sans-serif !important;
                font-size: {conf.FontSize}px !important;
                background-color: {cssBgColor} !important;
                border-radius: 8px;
                padding: 15px;
                text-align: center;
                {textOutline}
                {boxStyles}
            }}

            /* Regra para o botão do 'olhinho' (Transparência) funcionar só na caixa de texto */
            body.transp-on:not(.transp-off) #text-box {{
                background-color: transparent !important;
                box-shadow: none !important;
            }}
        `;
    ";
            webView.CoreWebView2.ExecuteScriptAsync(script);

            // Força a atualização da margem logo a seguir
            ApplyPositionState();
        }

        private async void LoadExtensions()
        {
            if (webView.CoreWebView2 == null) return;
            var profile = webView.CoreWebView2.Profile;

            var loadedExtensions = await profile.GetBrowserExtensionsAsync();
            var enabledPaths = _configService.CurrentConfig.Overlay.CustomExtensions;

            // Get names of extensions we WANT to have enabled
            var targetExtensions = new List<(string Path, string Name)>();
            foreach (var originalPath in enabledPaths)
            {
                string pathToLoad = originalPath;

                // Auto-Heal: Tenta encontrar a versão mais recente na pasta "pai" (ID da extensão)
                try
                {
                    string parentDir = Directory.GetParent(originalPath)?.FullName;
                    if (parentDir != null && Directory.Exists(parentDir))
                    {
                        var versionDirs = Directory.GetDirectories(parentDir);
                        if (versionDirs.Length > 0)
                        {
                            pathToLoad = versionDirs.OrderByDescending(d => d).First();
                        }
                    }
                }
                catch { /* Fallback silencioso para o caminho original se der erro */ }

                if (Directory.Exists(pathToLoad))
                {
                    var info = VN2Anki.Helpers.BrowserExtensionHelper.GetExtensionsFromPath(pathToLoad).FirstOrDefault();
                    if (info != null) targetExtensions.Add((pathToLoad, info.Name));
                }
            }

            // 1. Remove extensions that are NOT in the target list
            foreach (var loadedExt in loadedExtensions)
            {
                if (!targetExtensions.Any(t => t.Name == loadedExt.Name))
                {
                    try { await loadedExt.RemoveAsync(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to remove browser extension {loadedExt.Name}: {ex.Message}"); }
                }
            }

            // 2. Add extensions that are in the target list but NOT yet loaded
            foreach (var target in targetExtensions)
            {
                if (!loadedExtensions.Any(l => l.Name == target.Name))
                {
                    try { await profile.AddBrowserExtensionAsync(target.Path); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to add browser extension from {target.Path}: {ex.Message}"); }
                }
            }
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
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error calculating mouse position in OverlayWindow: {ex.Message}");
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
            this.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0));
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
            _windowService.OpenExtensionsManager(this);
        }

        private void BtnPosition_Click(object sender, RoutedEventArgs e)
        {
            _isTextAtTop = !_isTextAtTop;
            _configService.CurrentConfig.Overlay.IsTextAtTop = _isTextAtTop;
            ApplyPositionState();
            WeakReferenceMessenger.Default.Send(new SaveOverlayStateMessage());
        }

        private void BtnTransparency_Click(object sender, RoutedEventArgs e)
        {
            _isTransparent = !_isTransparent;
            _configService.CurrentConfig.Overlay.IsTransparent = _isTransparent;
            ApplyTransparencyState();
            WeakReferenceMessenger.Default.Send(new SaveOverlayStateMessage());
        }

        private void BtnPassThrough_Click(object sender, RoutedEventArgs e)
        {
            _isPassThroughToggled = !_isPassThroughToggled;
            _configService.CurrentConfig.Overlay.IsPassThrough = _isPassThroughToggled;
            ApplyPassThroughState();
            WeakReferenceMessenger.Default.Send(new SaveOverlayStateMessage());
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            var conf = _configService.CurrentConfig.Overlay;
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
                BtnMaximize.Content = "🗖";
                conf.IsMaximized = false;
            }
            else
            {
                this.WindowState = WindowState.Maximized;
                BtnMaximize.Content = "🗗";
                conf.IsMaximized = true;
            }
            WeakReferenceMessenger.Default.Send(new SaveOverlayStateMessage());
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => this.Close();

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void OverlayWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var conf = _configService.CurrentConfig.Overlay;

            // 1. SEMPRE aplica o tamanho e posição (isso serve de "RestoreBounds" caso ela abra maximizada)
            if (!double.IsInfinity(conf.Width) && !double.IsNaN(conf.Width) && conf.Width > 0)
                this.Width = conf.Width;

            if (!double.IsInfinity(conf.Height) && !double.IsNaN(conf.Height) && conf.Height > 0)
                this.Height = conf.Height;

            if (!double.IsNaN(conf.Top) && !double.IsInfinity(conf.Top) && !double.IsNaN(conf.Left) && !double.IsInfinity(conf.Left))
            {
                this.Top = conf.Top;
                this.Left = conf.Left;
            }

            // 2. SÓ DEPOIS aplica o estado de maximizado
            if (conf.IsMaximized)
            {
                this.WindowState = WindowState.Maximized;
                if (BtnMaximize != null) BtnMaximize.Content = "🗗";
            }
            else
            {
                this.WindowState = WindowState.Normal;
                if (BtnMaximize != null) BtnMaximize.Content = "🗖";
            }

            ApplyPassThroughState();

            // 3. Liberta a trava: a partir de agora, qualquer movimento do utilizador é real e deve ser guardado
            _isLoaded = true;

            if (this.WindowState == WindowState.Normal)
            {
                _lastNormalTop = this.Top;
                _lastNormalLeft = this.Left;
                _lastNormalWidth = this.Width;
                _lastNormalHeight = this.Height;
            }
        }

        private void OverlayWindow_LocationOrSizeChanged(object? sender, EventArgs e)
        {
            // Bloqueia leituras enquanto o WPF ainda está a construir a janela
            if (!_isLoaded) return;

            var conf = _configService.CurrentConfig.Overlay;

            if (this.WindowState == WindowState.Maximized)
            {
                conf.IsMaximized = true;
            }
            else if (this.WindowState == WindowState.Normal)
            {
                conf.IsMaximized = false;

                _lastNormalTop = this.Top;
                _lastNormalLeft = this.Left;
                _lastNormalWidth = this.Width;
                _lastNormalHeight = this.Height;

                conf.Width = this.Width;
                conf.Height = this.Height;
                conf.Top = this.Top;
                conf.Left = this.Left;
            }
        }

        private void OverlayWindow_Closed(object? sender, EventArgs e)
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

            _configService.Save(); // Mantém o save global do disco
            WeakReferenceMessenger.Default.Send(new SaveOverlayStateMessage()); // Dispara o save no Banco de Dados

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

        // this method will be called whenever the OverlayConfigUpdatedMessage is sent, allowing the overlay to update its appearance in real-time based on configuration changes
        public void Receive(OverlayConfigUpdatedMessage message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _isLoaded = false; // Tranca a leitura
                var conf = _configService.CurrentConfig.Overlay;

                // 1. Posições e Tamanho Base
                if (!double.IsInfinity(conf.Width) && !double.IsNaN(conf.Width) && conf.Width > 0)
                    this.Width = conf.Width;

                if (!double.IsInfinity(conf.Height) && !double.IsNaN(conf.Height) && conf.Height > 0)
                    this.Height = conf.Height;

                if (!double.IsNaN(conf.Top) && !double.IsInfinity(conf.Top) && !double.IsNaN(conf.Left) && !double.IsInfinity(conf.Left))
                {
                    this.Top = conf.Top;
                    this.Left = conf.Left;
                }

                // 2. Estado de Maximização
                if (conf.IsMaximized)
                {
                    this.WindowState = WindowState.Maximized;
                    if (BtnMaximize != null) BtnMaximize.Content = "🗗";
                }
                else
                {
                    this.WindowState = WindowState.Normal;
                    if (BtnMaximize != null) BtnMaximize.Content = "🗖";
                }

                // 3. Restaura as variáveis de estado
                _isTextAtTop = conf.IsTextAtTop;
                _isTransparent = conf.IsTransparent;
                _isPassThroughToggled = conf.IsPassThrough;

                // 4. Atualiza visuais
                UpdateBackground();
                ApplyDynamicStyles();
                ApplyTransparencyState();
                ApplyPositionState();
                ApplyPassThroughState();

                _isLoaded = true; // Destranca a leitura

                if (this.WindowState == WindowState.Normal)
                {
                    _lastNormalTop = this.Top;
                    _lastNormalLeft = this.Left;
                    _lastNormalWidth = this.Width;
                    _lastNormalHeight = this.Height;
                }
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
