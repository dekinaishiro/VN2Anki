using System.Threading;
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
using Microsoft.Extensions.DependencyInjection;
using VN2Anki.Data;
using VN2Anki.Models.Entities;

namespace VN2Anki.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject, IRecipient<StatusMessage>, IRecipient<PlayVnMessage>
    {
        private readonly MiningService _miningService;
        private readonly IConfigurationService _configService;
        private readonly AnkiExportService _ankiExportService;
        private readonly AnkiHandler _ankiHandler;
        private CancellationTokenSource _pollingCts;

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

        private readonly VideoEngine _videoEngine;

        [ObservableProperty]
        private VN2Anki.Models.Entities.VisualNovel _currentVN;

        public string BufferBtnText => IsBufferActive ? "ON" : "OFF";
        public Brush BufferBtnBackground => IsBufferActive ? Brushes.Green : Brushes.Crimson;

        public MainWindowViewModel(SessionTracker tracker, MiningService miningService, IConfigurationService configService, AnkiExportService ankiExportService, AnkiHandler ankiHandler, VideoEngine videoEngine)
        {
            Tracker = tracker;
            _miningService = miningService;
            _configService = configService;
            _ankiExportService = ankiExportService;
            _ankiHandler = ankiHandler;
            _videoEngine = videoEngine;

            _miningService.OnBufferStoppedUnexpectedly += () =>
            {
                Application.Current.Dispatcher.Invoke(() => IsBufferActive = false);
            };

            WeakReferenceMessenger.Default.RegisterAll(this); // listen to multiple message types
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
            // real vn and session validation: only saves if there's a valid VN and some activity
            if (CurrentVN != null && (Tracker.Elapsed.TotalSeconds > 0 || Tracker.ValidCharacterCount > 0))
            {
                using (var scope = App.Current.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var record = new SessionRecord
                    {
                        VisualNovelId = CurrentVN.Id,
                        StartTime = System.DateTime.Now - Tracker.Elapsed,
                        EndTime = System.DateTime.Now,
                        DurationSeconds = (int)Tracker.Elapsed.TotalSeconds,
                        CharactersRead = Tracker.ValidCharacterCount,
                        CardsMined = 0 // Expansão futura
                    };
                    db.Sessions.Add(record);
                    db.SaveChanges();
                }

                // tells hub to update list
                WeakReferenceMessenger.Default.Send(new SessionSavedMessage());
            }

            if (IsBufferActive) ToggleBuffer();
            Tracker.Reset();
            foreach (var slot in _miningService.HistorySlots) slot.Dispose();
            _miningService.HistorySlots.Clear();

            CurrentVN = null;

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

        public async void Receive(PlayVnMessage message)
        {
            var vn = message.VisualNovel;

            // verifies if current session is active and prompts the user to confirm if they want to end it before starting a new one
            if (CurrentVN != null && (Tracker.ValidCharacterCount > 0 || Tracker.Elapsed.TotalSeconds > 0 || IsBufferActive))
            {
                var result = MessageBox.Show($"Uma sessão está ativa com '{CurrentVN.Title}'.\nDeseja encerrá-la e iniciar outra com '{vn.Title}'?", "Atenção", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    EndSession();
                }
                else
                {
                    return;
                }
            }

            // polling duplicate prevention: cancels any existing polling loop before starting a new one
            _pollingCts?.Cancel();
            _pollingCts = new CancellationTokenSource();
            var token = _pollingCts.Token;

            CurrentVN = vn;

            if (IsBufferActive) ToggleBuffer();

            if (string.IsNullOrEmpty(vn.ExecutablePath) || !System.IO.File.Exists(vn.ExecutablePath))
            {
                WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Executável não encontrado!", IsError = true }));
                CurrentVN = null;
                return;
            }

            try
            {
                var pInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = vn.ExecutablePath,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(vn.ExecutablePath),
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(pInfo);
            }
            catch (System.Exception)
            {
                WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Erro ao abrir o jogo!", IsError = true }));
                CurrentVN = null;
                return;
            }

            WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = $"Aguardando janela: {vn.Title}...", IsError = false }));

            bool windowFound = false;

            try
            {
                // token injected delay loop to wait for the game window to appear
                // timeout is harded coded for now
                for (int i = 0; i < 30; i++)
                {
                    await Task.Delay(1000, token); // Passa o token para a espera!

                    var windows = _videoEngine.GetWindows();
                    var targetWin = windows.FirstOrDefault(w => w.ExecutablePath == vn.ExecutablePath || w.ProcessName == vn.ProcessName);

                    if (targetWin != null)
                    {
                        var config = _configService.CurrentConfig;
                        config.Media.VideoWindow = targetWin.ProcessName;
                        _configService.Save();

                        _miningService.TargetVideoWindow = targetWin.ProcessName;
                        windowFound = true;

                        WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Vídeo conectado! Pronto para o Play.", IsError = false }));
                        break;
                    }
                }
            }
            catch (TaskCanceledException)
            {
                // prevents overlapping polls
                return;
            }

            if (!windowFound)
            {
                WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Timeout: A janela não apareceu.", IsError = true }));
                CurrentVN = null;

                var config = _configService.CurrentConfig;
                config.Media.VideoWindow = string.Empty;
                _configService.Save();
                _miningService.TargetVideoWindow = string.Empty;
            }
        }
    }
}