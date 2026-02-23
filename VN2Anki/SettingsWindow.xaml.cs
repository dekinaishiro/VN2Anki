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
        private AppConfig _config;

        public SettingsWindow(MiningService miningService, AppConfig currentConfig)
        {
            InitializeComponent();
            _miningService = miningService;
            _config = currentConfig;

            this.Loaded += async (s, e) => await InitializeDataAsync();
        }

        private async Task InitializeDataAsync()
        {
            await RefreshAudioAsync();
            RefreshVideo();
            await LoadAnkiDataAsync();

            if (!string.IsNullOrEmpty(_config.AudioDevice))
                ComboAudio.SelectedItem = ComboAudio.Items.Cast<AudioDeviceItem>().FirstOrDefault(x => x.Name == _config.AudioDevice);
            if (!string.IsNullOrEmpty(_config.VideoWindow))
                ComboVideo.SelectedValue = _config.VideoWindow;
            if (!string.IsNullOrEmpty(_config.Deck) && ComboDeck.Items.Contains(_config.Deck))
                ComboDeck.SelectedItem = _config.Deck;
            if (!string.IsNullOrEmpty(_config.Model) && ComboModel.Items.Contains(_config.Model))
                ComboModel.SelectedItem = _config.Model;

            TxtIdleTime.Text = _config.IdleTime ?? "30";
            TxtMaxSlots.Text = _config.MaxSlots ?? "50";
            ChkDynamicTimeout.IsChecked = _config.UseDynamicTimeout;
            ChkOpenSettings.IsChecked = _config.OpenSettingsOnStartup;
            ComboLanguage.SelectedItem = ComboLanguage.Items.Cast<ComboBoxItem>().FirstOrDefault(x => x.Tag.ToString() == _config.Language);

            string tagTarget = _config.MaxImageWidth.ToString();
            ComboImageRes.SelectedItem = ComboImageRes.Items.Cast<ComboBoxItem>().FirstOrDefault(x => x.Tag.ToString() == tagTarget)
                                         ?? ComboImageRes.Items[2];
        }

        private async Task RefreshAudioAsync()
        {
            var audioDevices = await Task.Run(() => _miningService.Audio.GetDevices());
            ComboAudio.ItemsSource = audioDevices;
        }
        private void BtnRefreshAudio_Click(object sender, RoutedEventArgs e) => _ = RefreshAudioAsync();

        private void RefreshVideo()
        {
            var windows = _miningService.Video.GetWindows();
            ComboVideo.ItemsSource = windows;
        }
        private void BtnRefreshVideo_Click(object sender, RoutedEventArgs e) => RefreshVideo();

        private async Task LoadAnkiDataAsync()
        {
            if (await _miningService.Anki.IsConnectedAsync())
            {
                ComboDeck.ItemsSource = await _miningService.Anki.GetDecksAsync();
                ComboModel.ItemsSource = await _miningService.Anki.GetModelsAsync();
            }
        }
        private void BtnRefreshAnki_Click(object sender, RoutedEventArgs e) => _ = LoadAnkiDataAsync();

        private async void ComboModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboModel.SelectedItem is string modelName)
            {
                var fields = await _miningService.Anki.GetModelFieldsAsync(modelName);
                ComboFieldAudio.ItemsSource = fields;
                ComboFieldImage.ItemsSource = fields;

                if (!string.IsNullOrEmpty(_config.AudioField) && fields.Contains(_config.AudioField))
                    ComboFieldAudio.SelectedItem = _config.AudioField;
                if (!string.IsNullOrEmpty(_config.ImageField) && fields.Contains(_config.ImageField))
                    ComboFieldImage.SelectedItem = _config.ImageField;
            }
        }
        private void ComboLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!this.IsLoaded) return;

            if (ComboLanguage.SelectedItem is ComboBoxItem item && item.Tag is string langCode)
            {
                if (_config.Language == langCode) return;

                _config.Language = langCode;

                ConfigManager.Save(_config);

                var result = MessageBox.Show(
                    "O aplicativo precisa ser reiniciado para aplicar o novo idioma. Reiniciar agora?\n\nThe application needs to restart to apply the new language. Restart now?",
                    "Idioma / Language",
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
            _config.AudioDevice = (ComboAudio.SelectedItem as AudioDeviceItem)?.Name;
            _config.VideoWindow = ComboVideo.SelectedValue?.ToString();
            _config.Deck = ComboDeck.SelectedItem?.ToString();
            _config.Model = ComboModel.SelectedItem?.ToString();
            _config.AudioField = ComboFieldAudio.SelectedItem?.ToString();
            _config.ImageField = ComboFieldImage.SelectedItem?.ToString();
            _config.IdleTime = TxtIdleTime.Text;
            _config.MaxSlots = TxtMaxSlots.Text;
            _config.UseDynamicTimeout = ChkDynamicTimeout.IsChecked ?? true;
            _config.OpenSettingsOnStartup = ChkOpenSettings.IsChecked ?? false;
            if (ComboImageRes.SelectedItem is ComboBoxItem resItem && int.TryParse(resItem.Tag.ToString(), out int parsedWidth))
            {
                _config.MaxImageWidth = parsedWidth;
            }

            ConfigManager.Save(_config);

            this.Close();
            //this.DialogResult = true;
        }

        private void ChkOpenSettings_Checked(object sender, RoutedEventArgs e)
        {

        }
    }
}