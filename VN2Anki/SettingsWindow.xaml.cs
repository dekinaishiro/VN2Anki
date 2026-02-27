using System.Linq;
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

            if (ComboImageRes.SelectedItem is ComboBoxItem resItem && int.TryParse(resItem.Tag.ToString(), out int parsedWidth))
            {
                config.Media.MaxImageWidth = parsedWidth;
            }
            if (ComboAudioRes.SelectedItem is ComboBoxItem audioItem && int.TryParse(audioItem.Tag.ToString(), out int parsedBitrate))
            {
                config.Media.AudioBitrate = parsedBitrate;
            }

            config.Anki.Url = TxtAnkiUrl.Text.Trim();
            if (int.TryParse(TxtAnkiTimeout.Text.Trim(), out int timeout) && timeout > 0)
            {
                config.Anki.TimeoutSeconds = timeout;
            }
            else
            {
                config.Anki.TimeoutSeconds = 15;
            }

            if (ComboHookType.SelectedItem is ComboBoxItem hookItem && int.TryParse(hookItem.Tag.ToString(), out int parsedHook))
            {
                hookConfig.ActiveHookType = parsedHook;
            }

            hookConfig.WebSocketUrl = TxtWsUrl.Text.Trim();

            _configService.Save();
            this.Close();
        }

        private void ChkOpenSettings_Checked(object sender, RoutedEventArgs e)
        {
        }
    }
}