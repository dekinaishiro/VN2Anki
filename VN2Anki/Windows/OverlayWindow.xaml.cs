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

        private OverlayWin32Manager _win32Manager;

        private bool _isTextAtTop = false;
        private bool _isTransparent = true;
        private bool _isPassThroughToggled = false;
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

            this.Loaded += OverlayWindow_Loaded;
            this.Closed += OverlayWindow_Closed;
            this.LocationChanged += OverlayWindow_LocationOrSizeChanged;
            this.SizeChanged += OverlayWindow_LocationOrSizeChanged;

            var conf = _configService.CurrentConfig.Overlay;
            _isTextAtTop = conf.IsTextAtTop;
            _isTransparent = conf.IsTransparent;
            _isPassThroughToggled = conf.IsPassThrough;

            _win32Manager = new OverlayWin32Manager(
                this,
                0xA2,
                () => _isPassThroughToggled,
                (finalPassThrough) => UpdatePassThroughVisuals(finalPassThrough)
            );
            UpdateModifierKey();

            InitializeWebViewAsync();

            WeakReferenceMessenger.Default.RegisterAll(this); 
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _win32Manager.Initialize(new WindowInteropHelper(this).Handle);
        }

        private void UpdatePassThroughVisuals(bool finalPassThrough)
        {
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

        private void UpdateModifierKey()
        {
            string mod = _configService.CurrentConfig.Overlay.PassThroughModifier;
            int vk = mod switch
            {
                "Alt" => 0xA4,
                "Shift" => 0xA0,
                _ => 0xA2
            };
            _win32Manager.SetModifierKey(vk);
        }

        private void ApplyPositionState()
        {
            if (webView.CoreWebView2 != null)
            {
                var conf = _configService.CurrentConfig.Overlay;
                
                var payload = new
                {
                    action = "updatePosition",
                    data = new
                    {
                        isTextAtTop = _isTextAtTop,
                        verticalMargin = conf.VerticalMargin,
                        horizontalDisplacement = conf.HorizontalDisplacement
                    }
                };

                webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload));
            }
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

            await VN2Anki.Helpers.BrowserExtensionHelper.SyncProfileExtensionsAsync(webView.CoreWebView2.Profile, _configService.CurrentConfig.Overlay.CustomExtensions);

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

                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                _win32Manager?.ForwardClick(sx, sy, btn);
                            });
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error handling click event in WebView: {ex.Message}"); }
            };

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string webFolder = Path.Combine(baseDir, "Assets", "Web");
            
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping("vn.local", webFolder, CoreWebView2HostResourceAccessKind.Allow);
            webView.CoreWebView2.Navigate($"http://vn.local/overlay.html?t={DateTime.Now.Ticks}");

            webView.NavigationCompleted += (s, e) =>
            {
                _win32Manager.InstallWebViewSubclass(webView.Handle);

                ApplyDynamicStyles();

                ApplyPositionState();
                ApplyTransparencyState();
            };
        }

        private void ApplyDynamicStyles()
        {
            if (webView.CoreWebView2 == null) return;

            var conf = _configService.CurrentConfig.Overlay;
            
            var payload = new
            {
                action = "updateStyle",
                data = new
                {
                    bgColor = VN2Anki.Helpers.ColorHelper.WpfHexToCss(conf.BgColor),
                    fontColor = VN2Anki.Helpers.ColorHelper.WpfHexToCss(conf.FontColor),
                    outlineColor = VN2Anki.Helpers.ColorHelper.WpfHexToCss(conf.OutlineColor),
                    useTextBoxMode = conf.UseTextBoxMode,
                    textVerticalAlignment = string.IsNullOrEmpty(conf.TextVerticalAlignment) ? "center" : conf.TextVerticalAlignment,
                    textBoxMinHeight = conf.TextBoxMinHeight,
                    textBoxWidthPercentage = conf.TextBoxWidthPercentage,
                    outlineThickness = conf.OutlineThickness,
                    fontFamily = conf.FontFamily,
                    fontSize = conf.FontSize
                }
            };

            webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload));

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



        private void ApplyPassThroughState()
        {
            bool finalPassThrough = _isPassThroughToggled ^ (_win32Manager?.IsHoldActive ?? false);
            UpdatePassThroughVisuals(finalPassThrough);
            _win32Manager?.ApplyWindowExStyle();
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

                var payload = new
                {
                    action = "updateTransparency",
                    data = new { isTransparent = _isTransparent }
                };
                webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload));

                if (_isTransparent)
                {
                    BtnTransparencia.Foreground = new SolidColorBrush(System.Windows.Media.Colors.White);
                }
                else
                {
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

            // Se estiver minimizado, ignora completamente para não salvar tamanhos de "ícone"
            if (this.WindowState == WindowState.Minimized) return;

            var conf = _configService.CurrentConfig.Overlay;

            if (this.WindowState == WindowState.Maximized)
            {
                conf.IsMaximized = true;
            }
            else if (this.WindowState == WindowState.Normal)
            {
                // PROTEÇÃO: Só atualiza se o tamanho for razoável. 
                // Se o Windows reportar algo minúsculo (bug de minimização), ignoramos.
                if (this.Width < 100 || this.Height < 40) return;

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

            _win32Manager?.Dispose();
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
            Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                if (webView.CoreWebView2 != null)
                {
                    await VN2Anki.Helpers.BrowserExtensionHelper.SyncProfileExtensionsAsync(webView.CoreWebView2.Profile, _configService.CurrentConfig.Overlay.CustomExtensions);
                }
            });
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

                var payload = new
                {
                    action = "newText",
                    data = new { text = safeText }
                };

                webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload));
            }, System.Windows.Threading.DispatcherPriority.Normal);
        }
    }
}
