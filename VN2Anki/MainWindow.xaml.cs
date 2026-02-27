using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using VN2Anki.Locales;
using VN2Anki.Models;
using VN2Anki.Services;
using VN2Anki.ViewModels;

namespace VN2Anki
{
    public partial class MainWindow : Window
    {
        private readonly MiningService _miningService;
        private readonly IConfigurationService _configService;
        private readonly MainWindowViewModel _viewModel;
        private SettingsWindow _settingsWindowInstance;
        private bool _isBufferActive = false;
        private readonly AnkiExportService _ankiExportService;
        private readonly AnkiHandler _ankiHandler;

        public MainWindow(MiningService miningService, IConfigurationService configService, MainWindowViewModel viewModel, AnkiExportService ankiExportService, AnkiHandler ankiHandler)
        {
            InitializeComponent();
            _miningService = miningService;
            _configService = configService;
            _viewModel = viewModel;
            _ankiExportService = ankiExportService;
            _ankiHandler = ankiHandler;

            this.DataContext = _viewModel;

            _miningService.OnBufferStoppedUnexpectedly += HandleUnexpectedBufferStop;

            this.Loaded += Window_Loaded;

            this.Closing += (s, e) =>
            {
                _miningService.StopBuffer();
                SaveWindowPosition();
            };
            _ankiExportService = ankiExportService;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyConfigToService();

            var config = _configService.CurrentConfig.General;

            // Restores last position or centers if not available
            if (!double.IsNaN(config.MainWindowTop) && !double.IsNaN(config.MainWindowLeft))
            {
                this.Top = config.MainWindowTop;
                this.Left = config.MainWindowLeft;
            }
            else
            {
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            if (config.OpenSettingsOnStartup)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    BtnOpenSettings_Click(this, new RoutedEventArgs());
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }

        private void SaveWindowPosition()
        {
            _configService.CurrentConfig.General.MainWindowTop = this.Top;
            _configService.CurrentConfig.General.MainWindowLeft = this.Left;
            _configService.Save();
        }

        private void ApplyConfigToService()
        {
            var config = _configService.CurrentConfig;

            _miningService.TargetVideoWindow = config.Media.VideoWindow;
            if (int.TryParse(config.Session.MaxSlots, out int parsedMax) && parsedMax > 0) _miningService.MaxSlots = parsedMax;
            if (double.TryParse(config.Session.IdleTime, out double parsedIdle) && parsedIdle > 0) _miningService.IdleTimeoutFixo = parsedIdle;

            _miningService.UseDynamicTimeout = config.Session.UseDynamicTimeout;
            _miningService.MaxImageWidth = config.Media.MaxImageWidth;

            _ankiHandler.UpdateSettings(config.Anki.Url, config.Anki.TimeoutSeconds);
        }

        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_settingsWindowInstance != null)
            {
                if (_settingsWindowInstance.WindowState == WindowState.Minimized)
                    _settingsWindowInstance.WindowState = WindowState.Normal;
                _settingsWindowInstance.Activate();
                return;
            }

            string oldAudioDevice = _configService.CurrentConfig.Media.AudioDevice;

            // Resolves window via DI container
            _settingsWindowInstance = App.Current.Services.GetRequiredService<SettingsWindow>();
            _settingsWindowInstance.Owner = this;

            _settingsWindowInstance.Closed += (s, args) =>
            {
                _settingsWindowInstance = null;
                _configService.Load();
                ApplyConfigToService();

                if (_isBufferActive && oldAudioDevice != _configService.CurrentConfig.Media.AudioDevice)
                {
                    BtnToggleBuffer_Click(null, null);
                    BtnToggleBuffer_Click(null, null);
                }
            };

            _settingsWindowInstance.Show();
        }

        private void BtnToggleBuffer_Click(object sender, RoutedEventArgs e)
        {
            if (!_isBufferActive)
            {
                var devices = _miningService.Audio.GetDevices();
                var deviceId = devices.FirstOrDefault(d => d.Name == _configService.CurrentConfig.Media.AudioDevice)?.Id;

                if (string.IsNullOrEmpty(deviceId))
                {
                    ShowFlashMessage("Configure o Áudio primeiro!", true);
                    return;
                }

                _miningService.StartBuffer(deviceId);
                _isBufferActive = true;
                BtnToggleBuffer.Content = "ON";
                BtnToggleBuffer.Background = Brushes.Green;
            }
            else
            {
                _miningService.StopBuffer();
                _isBufferActive = false;
                BtnToggleBuffer.Content = "OFF";
                BtnToggleBuffer.Background = Brushes.Crimson;
            }
        }

        private void DragBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) this.DragMove(); }

        private void BtnToggleStats_Click(object sender, RoutedEventArgs e)
        {
            MiniStatsPanel.Visibility = MiniStatsPanel.Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnEndSession_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(Strings.MsgConfirmEndSession, Strings.MsgAttention, MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                if (_isBufferActive) BtnToggleBuffer_Click(null, null);
                _miningService.Tracker.Reset();

               foreach (var slot in _miningService.HistorySlots)
                {
                    slot.Dispose();
                }

                _miningService.HistorySlots.Clear();
                TxtStatus.Text = Strings.StatusSessionEnded;
            }
        }

        private void BtnOpenHistory_Click(object sender, RoutedEventArgs e)
            => MiningWindow.ShowWindow(_miningService.HistorySlots, async slot => await ProcessMiningToAnki(slot), slot => _miningService.DeleteSlot(slot));

        // quick add overlay (add last mined slot to last anki card)
        private async void BtnMiniQuickAdd_Click(object sender, RoutedEventArgs e)
        {
            if (_miningService.HistorySlots.Count == 0) { ShowFlashMessage(Strings.MsgEmpty, true); return; }
            BtnMiniQuickAdd.IsEnabled = false; BtnMiniQuickAdd.Background = Brushes.Cyan;

            await ProcessMiningToAnki(_miningService.HistorySlots[0], true);

            BtnMiniQuickAdd.Background = Brushes.DodgerBlue; BtnMiniQuickAdd.IsEnabled = true;
        }

        private void BtnOpenOverlay_Click(object sender, RoutedEventArgs e)
        {
            var overlay = App.Current.Services.GetRequiredService<OverlayWindow>();
            overlay.Show();
        }

        // 
        public async Task ProcessMiningToAnki(MiningSlot slot, bool isQuietMode = false)
        {
            var config = _configService.CurrentConfig;

            if (string.IsNullOrEmpty(config.Anki.Deck))
            {
                ShowFlashMessage("Please configure the Deck first!", true);
                return;
            }

            var result = await _ankiExportService.ExportSlotAsync(slot, config.Anki, config.Media);

            if (isQuietMode) ShowFlashMessage(result.success ? "Card Updated" : "Error Updating Card", !result.success);
            else if (result.success) MessageBox.Show("Success!");
            else MessageBox.Show(result.message, "Attention");
        }

        private void ShowFlashMessage(string message, bool isError = false)
        {
            Dispatcher.Invoke(() =>
            {
                var f = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = isError ? Brushes.Red : Brushes.Green, Topmost = true, ShowInTaskbar = false, Width = 250, Height = 40, Left = this.Left, Top = this.Top + this.Height + 5 };
                f.Content = new TextBlock { Text = message, Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                f.Show();
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                t.Tick += (s, args) => { f.Close(); t.Stop(); f = null; }; t.Start();
            });
        }

        private void BtnExitApp_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void HandleUnexpectedBufferStop()
        {
            Dispatcher.Invoke(() =>
            {
                _isBufferActive = false;
                BtnToggleBuffer.Content = "OFF";
                BtnToggleBuffer.Background = System.Windows.Media.Brushes.Crimson;
            });
        }
    }
}