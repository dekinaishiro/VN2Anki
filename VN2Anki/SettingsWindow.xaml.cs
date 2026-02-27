using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VN2Anki.Models;
using VN2Anki.Services;

namespace VN2Anki
{
    public partial class SettingsWindow : Window
    {
        private readonly MiningService _miningService;
        private readonly IConfigurationService _configService;
        private readonly AudioEngine _audio;
        private readonly VideoEngine _video;
        private readonly AnkiHandler _anki;

        public SettingsWindow(MiningService miningService, IConfigurationService configService, AudioEngine audio, VideoEngine video, AnkiHandler anki)
        {
            InitializeComponent();
            _miningService = miningService;
            _configService = configService;
            _audio = audio;
            _video = video;
            _anki = anki;

            this.Loaded += async (s, e) => await InitializeDataAsync();
        }

        private async Task InitializeDataAsync()
        {
            await RefreshAudioAsync();
            RefreshVideo();
            await LoadAnkiDataAsync();

            var config = _configService.CurrentConfig;
            var hookConfig = _configService.CurrentConfig.Hook;
            string tagHook = hookConfig.ActiveHookType.ToString();
            ComboHookType.SelectedItem = ComboHookType.Items.Cast<ComboBoxItem>().FirstOrDefault(x => x.Tag.ToString() == tagHook)
                                         ?? ComboHookType.Items[0];
            TxtWsUrl.Text = string.IsNullOrEmpty(hookConfig.WebSocketUrl) ? "ws://localhost:2333/api/ws/text/origin" : hookConfig.WebSocketUrl;

            if (!string.IsNullOrEmpty(config.Media.AudioDevice))
                ComboAudio.SelectedItem = ComboAudio.Items.Cast<AudioDeviceItem>().FirstOrDefault(x => x.Name == config.Media.AudioDevice);
            if (!string.IsNullOrEmpty(config.Media.VideoWindow))
                ComboVideo.SelectedValue = config.Media.VideoWindow;
            if (!string.IsNullOrEmpty(config.Anki.Deck) && ComboDeck.Items.Contains(config.Anki.Deck))
                ComboDeck.SelectedItem = config.Anki.Deck;
            if (!string.IsNullOrEmpty(config.Anki.Model) && ComboModel.Items.Contains(config.Anki.Model))
                ComboModel.SelectedItem = config.Anki.Model;

            TxtIdleTime.Text = config.Session.IdleTime ?? "20";
            TxtMaxSlots.Text = config.Session.MaxSlots ?? "30";
            ChkDynamicTimeout.IsChecked = config.Session.UseDynamicTimeout;
            ChkOpenSettings.IsChecked = config.General.OpenSettingsOnStartup;
            ComboLanguage.SelectedItem = ComboLanguage.Items.Cast<ComboBoxItem>().FirstOrDefault(x => x.Tag.ToString() == config.General.Language);

            string tagTarget = config.Media.MaxImageWidth.ToString();
            ComboImageRes.SelectedItem = ComboImageRes.Items.Cast<ComboBoxItem>().FirstOrDefault(x => x.Tag.ToString() == tagTarget)
                                         ?? ComboImageRes.Items[2];

            string tagAudio = config.Media.AudioBitrate.ToString();
            ComboAudioRes.SelectedItem = ComboAudioRes.Items.Cast<ComboBoxItem>().FirstOrDefault(x => x.Tag.ToString() == tagAudio)
                                         ?? ComboAudioRes.Items[2];

            TxtAnkiUrl.Text = string.IsNullOrEmpty(config.Anki.Url) ? "http://127.0.0.1:8765" : config.Anki.Url;
            TxtAnkiTimeout.Text = config.Anki.TimeoutSeconds > 0 ? config.Anki.TimeoutSeconds.ToString() : "15";

            // Overlay Config
            var overlayConfig = _configService.CurrentConfig.Overlay;
            TxtBgColor.Text = overlayConfig.BgColor;
            TxtFontColor.Text = overlayConfig.FontColor;
            TxtFontSize.Text = overlayConfig.FontSize.ToString();

            ComboPassModifier.SelectedItem = ComboPassModifier.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(x => x.Tag.ToString() == overlayConfig.PassThroughModifier) ?? ComboPassModifier.Items[0];

            ListExtensions.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<string>(overlayConfig.CustomExtensions);
        }

        private async Task RefreshAudioAsync()
        {
            var audioDevices = await Task.Run(() => _audio.GetDevices()); 
            ComboAudio.ItemsSource = audioDevices;
        }
        private void BtnRefreshAudio_Click(object sender, RoutedEventArgs e) => _ = RefreshAudioAsync();

        private void RefreshVideo()
        {
            var windows = _video.GetWindows();
            ComboVideo.ItemsSource = windows;
        }
        private void BtnRefreshVideo_Click(object sender, RoutedEventArgs e) => RefreshVideo();

        private async Task LoadAnkiDataAsync()
        {
            if (await _anki.IsConnectedAsync()) // <-- Mudou aqui
            {
                ComboDeck.ItemsSource = await _anki.GetDecksAsync(); // <-- Mudou aqui
                ComboModel.ItemsSource = await _anki.GetModelsAsync(); // <-- Mudou aqui
            }
        }
        private void BtnRefreshAnki_Click(object sender, RoutedEventArgs e) => _ = LoadAnkiDataAsync();

        private async void ComboModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboModel.SelectedItem is string modelName)
            {
                var fields = await _anki.GetModelFieldsAsync(modelName);
                ComboFieldAudio.ItemsSource = fields;
                ComboFieldImage.ItemsSource = fields;

                var config = _configService.CurrentConfig.Anki;

                if (!string.IsNullOrEmpty(config.AudioField) && fields.Contains(config.AudioField))
                    ComboFieldAudio.SelectedItem = config.AudioField;
                if (!string.IsNullOrEmpty(config.ImageField) && fields.Contains(config.ImageField))
                    ComboFieldImage.SelectedItem = config.ImageField;
            }
        }

        private void ComboLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!this.IsLoaded) return;

            if (ComboLanguage.SelectedItem is ComboBoxItem item && item.Tag is string langCode)
            {
                if (_configService.CurrentConfig.General.Language == langCode) return;

                _configService.CurrentConfig.General.Language = langCode;
                _configService.Save();

                var result = MessageBox.Show(
                    Locales.Strings.LangRestartNow,
                    Locales.Strings.LblLanguage,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    System.Diagnostics.Process.Start(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
                    Application.Current.Shutdown();
                }
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var config = _configService.CurrentConfig;
            var hookConfig = _configService.CurrentConfig.Hook;

            config.Media.AudioDevice = (ComboAudio.SelectedItem as AudioDeviceItem)?.Name;
            config.Media.VideoWindow = ComboVideo.SelectedValue?.ToString();
            config.Anki.Deck = ComboDeck.SelectedItem?.ToString();
            config.Anki.Model = ComboModel.SelectedItem?.ToString();
            config.Anki.AudioField = ComboFieldAudio.SelectedItem?.ToString();
            config.Anki.ImageField = ComboFieldImage.SelectedItem?.ToString();

            config.Session.IdleTime = TxtIdleTime.Text;
            config.Session.MaxSlots = TxtMaxSlots.Text;
            config.Session.UseDynamicTimeout = ChkDynamicTimeout.IsChecked ?? true;
            config.General.OpenSettingsOnStartup = ChkOpenSettings.IsChecked ?? false;

            // video&audio
            if (ComboImageRes.SelectedItem is ComboBoxItem resItem && int.TryParse(resItem.Tag.ToString(), out int parsedWidth))
            {
                config.Media.MaxImageWidth = parsedWidth;
            }
            if (ComboAudioRes.SelectedItem is ComboBoxItem audioItem && int.TryParse(audioItem.Tag.ToString(), out int parsedBitrate))
            {
                config.Media.AudioBitrate = parsedBitrate;
            }

            // ankiUrl
            config.Anki.Url = TxtAnkiUrl.Text.Trim();
            if (int.TryParse(TxtAnkiTimeout.Text.Trim(), out int timeout) && timeout > 0)
            {
                config.Anki.TimeoutSeconds = timeout;
            }
            else
            {
                config.Anki.TimeoutSeconds = 15;
            }

            // hook
            if (ComboHookType.SelectedItem is ComboBoxItem hookItem && int.TryParse(hookItem.Tag.ToString(), out int parsedHook))
            {
                hookConfig.ActiveHookType = parsedHook;
            }

            hookConfig.WebSocketUrl = TxtWsUrl.Text.Trim();

            // overlay
            var overlayConfig = _configService.CurrentConfig.Overlay;
            overlayConfig.BgColor = TxtBgColor.Text.Trim();
            overlayConfig.FontColor = TxtFontColor.Text.Trim();
            if (int.TryParse(TxtFontSize.Text.Trim(), out int fSize)) overlayConfig.FontSize = fSize;

            if (ComboPassModifier.SelectedItem is ComboBoxItem modItem)
            {
                overlayConfig.PassThroughModifier = modItem.Tag.ToString();
            }

            overlayConfig.CustomExtensions = ListExtensions.Items.Cast<string>().ToList();

            // save
            _configService.Save();
            this.Close();
        }

        private void BtnAddExtension_Click(object sender, RoutedEventArgs e)
        {
            // Auto-detect the default extension path to save the user time
            string defaultChromePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\User Data\Default\Extensions");
            string defaultEdgePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\User Data\Default\Extensions");

            string startPath = Directory.Exists(defaultChromePath) ? defaultChromePath :
                               (Directory.Exists(defaultEdgePath) ? defaultEdgePath : Environment.GetFolderPath(Environment.SpecialFolder.Desktop));

            // Native WPF Folder Picker Hack
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select the Unpacked Extension Folder",
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Select Folder", // Dummy filename
                Filter = "Folders|\n",      // Hides files
                InitialDirectory = startPath
            };

            // Important: We pass 'this' so the dialog parents to the Settings Window
            if (dialog.ShowDialog(this) == true)
            {
                // Extracts just the folder path from the dummy filename
                string extPath = System.IO.Path.GetDirectoryName(dialog.FileName);

                if (!string.IsNullOrWhiteSpace(extPath) && System.IO.Directory.Exists(extPath))
                {
                    var list = ListExtensions.ItemsSource as System.Collections.ObjectModel.ObservableCollection<string>;
                    if (!list.Contains(extPath)) list.Add(extPath);
                }
            }
        }

        private void BtnExtensionSettings_Click(object sender, RoutedEventArgs e)
        {
            string selectedExtPath = ListExtensions.SelectedItem as string;
            bool isCustomExtSelected = !string.IsNullOrEmpty(selectedExtPath) && Directory.Exists(selectedExtPath);

            var settingsWin = new Window
            {
                Title = isCustomExtSelected ? "Extension Settings" : "Yomitan Settings",
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

                if (isCustomExtSelected)
                {
                    try
                    {
                        // 1. Load the custom extension and capture the dynamically generated ID
                        var extension = await settingsWebView.CoreWebView2.Profile.AddBrowserExtensionAsync(selectedExtPath);
                        string extensionId = extension.Id;

                        // 2. Parse manifest.json to find the correct settings page
                        string manifestPath = Path.Combine(selectedExtPath, "manifest.json");
                        string optionsHtmlPage = "index.html"; // Fallback

                        if (File.Exists(manifestPath))
                        {
                            string json = File.ReadAllText(manifestPath);
                            using (var doc = JsonDocument.Parse(json))
                            {
                                if (doc.RootElement.TryGetProperty("options_ui", out JsonElement optionsUi) && optionsUi.TryGetProperty("page", out JsonElement page))
                                {
                                    optionsHtmlPage = page.GetString();
                                }
                                else if (doc.RootElement.TryGetProperty("options_page", out JsonElement optPage))
                                {
                                    optionsHtmlPage = optPage.GetString();
                                }
                            }
                        }

                        // 3. Navigate to the dynamic URL
                        settingsWebView.CoreWebView2.Navigate($"chrome-extension://{extensionId}/{optionsHtmlPage}");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Could not load custom extension settings:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    // Fallback to Native Yomitan Load
                    string localAppData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
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
                                try { await settingsWebView.CoreWebView2.Profile.AddBrowserExtensionAsync(latestVersion); break; } catch { }
                            }
                        }
                    }

                    settingsWebView.CoreWebView2.Navigate($"chrome-extension://{yomitanId}/settings.html");
                }
            };

            settingsWin.Show();
        }

        private void BtnRemoveExtension_Click(object sender, RoutedEventArgs e)
        {
            if (ListExtensions.SelectedItem is string selectedExt)
            {
                var list = ListExtensions.ItemsSource as System.Collections.ObjectModel.ObservableCollection<string>;
                list.Remove(selectedExt);
            }
        }

        private void ChkOpenSettings_Checked(object sender, RoutedEventArgs e)
        {
        }

    }
}