using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using VN2Anki.Locales;
using VN2Anki.Messages;
using VN2Anki.Services;

namespace VN2Anki.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject, IRecipient<StatusMessage>
    {
        private readonly MiningService _miningService;
        private readonly IConfigurationService _configService;
        private readonly AnkiExportService _ankiExportService;
        private readonly AnkiHandler _ankiHandler;

        public SessionTracker Tracker { get; }

        [ObservableProperty]
        private string _statusText = "";

        [ObservableProperty]
        private Visibility _statusVisibility = Visibility.Collapsed;

        [ObservableProperty]
        private Visibility _miniStatsVisibility = Visibility.Visible;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(BufferBtnText))]
        [NotifyPropertyChangedFor(nameof(BufferBtnBackground))]
        private bool _isBufferActive;

        public string BufferBtnText => IsBufferActive ? "ON" : "OFF";
        public Brush BufferBtnBackground => IsBufferActive ? Brushes.Green : Brushes.Crimson;

        public MainWindowViewModel(SessionTracker tracker, MiningService miningService, IConfigurationService configService, AnkiExportService ankiExportService, AnkiHandler ankiHandler)
        {
            Tracker = tracker;
            _miningService = miningService;
            _configService = configService;
            _ankiExportService = ankiExportService;
            _ankiHandler = ankiHandler;

            _miningService.OnBufferStoppedUnexpectedly += () =>
            {
                Application.Current.Dispatcher.Invoke(() => IsBufferActive = false);
            };

            WeakReferenceMessenger.Default.Register(this);
        }

        public void ApplyConfigToServices()
        {
            var config = _configService.CurrentConfig;
            _miningService.TargetVideoWindow = config.Media.VideoWindow;
            if (int.TryParse(config.Session.MaxSlots, out int parsedMax) && parsedMax > 0) _miningService.MaxSlots = parsedMax;
            if (double.TryParse(config.Session.IdleTime, out double parsedIdle) && parsedIdle > 0) _miningService.IdleTimeoutFixo = parsedIdle;

            _miningService.UseDynamicTimeout = config.Session.UseDynamicTimeout;
            _miningService.MaxImageWidth = config.Media.MaxImageWidth;
            _ankiHandler.UpdateSettings(config.Anki.Url, config.Anki.TimeoutSeconds);
        }

        [RelayCommand]
        private void ToggleBuffer()
        {
            if (!IsBufferActive)
            {
                var config = _configService.CurrentConfig;

                if (string.IsNullOrEmpty(config.Media.VideoWindow))
                {
                    WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Video Source vazia! Selecione o vídeo.", IsError = true }));
                    return;
                }

                // checks if the process is still running, if not, clear the config and alert the user
                var procs = System.Diagnostics.Process.GetProcessesByName(config.Media.VideoWindow);
                bool isRunning = procs.Any(p => p.MainWindowHandle != IntPtr.Zero);
                foreach (var p in procs) p.Dispose();

                if (!isRunning)
                {
                    WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "A janela alvo está fechada! Abra o jogo e tente novamente.", IsError = true }));
                    return;
                }

                var devices = _miningService.Audio.GetDevices();
                var deviceId = devices.FirstOrDefault(d => d.Name == config.Media.AudioDevice)?.Id;

                if (string.IsNullOrEmpty(deviceId))
                {
                    WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Configure o Áudio primeiro!", IsError = true }));
                    return;
                }

                _miningService.StartBuffer(deviceId);
                IsBufferActive = true;
            }
            else
            {
                _miningService.StopBuffer();
                IsBufferActive = false;
            }
        }

        [RelayCommand]
        private void ToggleStats()
        {
            MiniStatsVisibility = MiniStatsVisibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        [RelayCommand]
        private async Task MiniQuickAddAsync()
        {
            if (_miningService.HistorySlots.Count == 0)
            {
                WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = Strings.MsgEmpty, IsError = true }));
                return;
            }

            var config = _configService.CurrentConfig;
            if (string.IsNullOrEmpty(config.Anki.Deck))
            {
                WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Please configure the Deck first!", IsError = true }));
                return;
            }

            var slot = _miningService.HistorySlots[0];
            var result = await _ankiExportService.ExportSlotAsync(slot, config.Anki, config.Media);

            WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload
            {
                Message = result.success ? "Card Updated" : "Error Updating",
                IsError = !result.success
            }));
        }

        public async Task ExportSlotAsync(Models.MiningSlot slot)
        {
            if (slot == null) return;

            var config = _configService.CurrentConfig;
            if (string.IsNullOrEmpty(config.Anki.Deck))
            {
                WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Please configure the Deck first!", IsError = true }));
                return;
            }

            var result = await _ankiExportService.ExportSlotAsync(slot, config.Anki, config.Media);

            WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload
            {
                Message = result.success ? "Card Updated" : "Error Updating",
                IsError = !result.success
            }));
        }

        public void EndSession()
        {
            if (IsBufferActive) ToggleBuffer();
            Tracker.Reset();
            foreach (var slot in _miningService.HistorySlots) slot.Dispose();
            _miningService.HistorySlots.Clear();
            StatusText = Strings.StatusSessionEnded;
            StatusVisibility = Visibility.Visible;
        }

        public void Receive(StatusMessage message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusText = message.Value;
                StatusVisibility = string.IsNullOrEmpty(message.Value) ? Visibility.Collapsed : Visibility.Visible;
            });
        }
    }
}