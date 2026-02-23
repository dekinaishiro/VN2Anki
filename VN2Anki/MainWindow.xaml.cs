using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VN2Anki.Locales;
using VN2Anki.Models;
using VN2Anki.Services;

namespace VN2Anki
{
    public partial class MainWindow : Window
    {
        private readonly MiningService _miningService;
        private AppConfig _currentConfig = new AppConfig();
        private bool _isBufferActive = false;

        public MainWindow(MiningService miningService)
        {
            InitializeComponent();
            _miningService = miningService;
            this.DataContext = _miningService.Tracker;
            _miningService.OnStatusChanged += msg => Dispatcher.Invoke(() => TxtStatus.Text = msg);
            _miningService.OnBufferStoppedUnexpectedly += HandleUnexpectedBufferStop;

            this.Loaded += Window_Loaded;

            // last pos
            this.Closing += (s, e) =>
            {
                _miningService.StopBuffer();
                SaveWindowPosition();
            };
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadConfig();

            // restore last position or center if not available
            if (!double.IsNaN(_currentConfig.MainWindowTop) && !double.IsNaN(_currentConfig.MainWindowLeft))
            {
                this.Top = _currentConfig.MainWindowTop;
                this.Left = _currentConfig.MainWindowLeft;
            }
            else
            {
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            if (_currentConfig.OpenSettingsOnStartup)
            {
                // ensures the main window is fully loaded before opening settings (avoids dropdown weird behavior x_x)
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    BtnOpenSettings_Click(this, new RoutedEventArgs());
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }

        private void SaveWindowPosition()
        {
            _currentConfig.MainWindowTop = this.Top;
            _currentConfig.MainWindowLeft = this.Left;

            ConfigManager.Save(_currentConfig); 
        }

        private void LoadConfig()
        {
            _currentConfig = ConfigManager.Load(); 
            ApplyConfigToService();
        }

        private void ApplyConfigToService()
        {
            _miningService.TargetVideoWindow = _currentConfig.VideoWindow;
            if (int.TryParse(_currentConfig.MaxSlots, out int parsedMax) && parsedMax > 0) _miningService.MaxSlots = parsedMax;
            if (double.TryParse(_currentConfig.IdleTime, out double parsedIdle) && parsedIdle > 0) _miningService.IdleTimeoutFixo = parsedIdle;
            _miningService.UseDynamicTimeout = _currentConfig.UseDynamicTimeout;
            _miningService.MaxImageWidth = _currentConfig.MaxImageWidth;
            _miningService.AudioBitrate = _currentConfig.AudioBitrate;
            _miningService.Anki.UpdateSettings(_currentConfig.AnkiUrl, _currentConfig.AnkiTimeout);
        }

        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var sw = new SettingsWindow(_miningService, _currentConfig);

            sw.Owner = this; // ensures that Settings stays abnove the main window

            sw.Closed += (s, args) => LoadConfig();

            sw.Show(); // instead of ShowDialog so the windows are independent
        }

        private void BtnToggleBuffer_Click(object sender, RoutedEventArgs e)
        {
            if (!_isBufferActive)
            {
                // gets the real device ID based on the name stored in config (since IDs can change between sessions)
                var devices = _miningService.Audio.GetDevices();
                var deviceId = devices.FirstOrDefault(d => d.Name == _currentConfig.AudioDevice)?.Id;

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

                // Chama o Dispose em vez de apenas limpar a mídia
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

        // 
        public async Task ProcessMiningToAnki(MiningSlot slot, bool isQuietMode = false)
        {
            if (string.IsNullOrEmpty(_currentConfig.Deck))
            {
                ShowFlashMessage("Configure o Deck!", true); return;
            }

            var result = await _miningService.ProcessMiningToAnki(slot, _currentConfig.Deck, _currentConfig.Model, _currentConfig.AudioField, _currentConfig.ImageField);

            TxtStatus.Text = result.success ? $"✅ {result.message}" : $"❌ {result.message}";
            if (isQuietMode) ShowFlashMessage(result.success ? Strings.MsgCardUpdated : Strings.MsgError, !result.success);
            else if (result.success) MessageBox.Show(Strings.MsgSuccess);
            else MessageBox.Show(result.message, Strings.MsgAttention);
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