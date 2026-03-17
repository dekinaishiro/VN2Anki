using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using VN2Anki.Data;
using VN2Anki.Locales;
using VN2Anki.Messages;
using VN2Anki.Models;
using VN2Anki.Models.Entities;
using VN2Anki.Services;
using VN2Anki.Services.Interfaces;


namespace VN2Anki.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject, IRecipient<StatusMessage>, IRecipient<PlayVnMessage>, IRecipient<BufferStoppedMessage>, IRecipient<SaveOverlayStateMessage>, IRecipient<SessionEndedMessage>
    {
        private readonly MiningService _miningService;
        private readonly IConfigurationService _configService;
        private readonly AnkiHandler _ankiHandler;
        private CancellationTokenSource? _pollingCts;
        private readonly ISessionManagerService _sessionManager;

        public bool HasUnsavedProgress => _sessionManager.HasUnsavedProgress;

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
        private VN2Anki.Models.Entities.VisualNovel? _currentVN;

        [ObservableProperty]
        private Visibility _manualLinkVisibility = Visibility.Collapsed;
        public string BufferBtnText => IsBufferActive ? "ON" : "OFF";
        public Brush BufferBtnBackground => IsBufferActive ? Brushes.Green : Brushes.Crimson;
        
        // main window title
        [ObservableProperty]
        private Brush _vnTitleColor = Brushes.Crimson;
        [ObservableProperty]
        private string _displayVnTitle = "No Video Source";
        // manual sync vn
        [ObservableProperty]
        private string _manualLinkText = "+";

        [ObservableProperty]
        private Brush _manualLinkColor = Brushes.Teal;

        // Semaphore properties
        [ObservableProperty]
        private string _videoIconKind = "MonitorOff";
        [ObservableProperty]
        private Brush _videoIconColor = Brushes.Crimson;

        [ObservableProperty]
        private string _audioIconKind = "VolumeOff";
        [ObservableProperty]
        private Brush _audioIconColor = Brushes.Crimson;

        [ObservableProperty]
        private string _linkIconKind = "LinkVariantOff";
        [ObservableProperty]
        private Brush _linkIconColor = Brushes.White;

        private bool _isFirstLoad = true;
        private readonly IWindowService _windowService;
        private readonly IGameLauncherService _gameLauncher;
        private readonly IVnDatabaseService _vnDatabaseService;
        private readonly IProcessMonitoringService _processMonitor;

        public MainWindowViewModel(SessionTracker tracker, MiningService miningService, IConfigurationService configService, AnkiHandler ankiHandler, VideoEngine videoEngine, IWindowService windowService, ISessionManagerService sessionManager, IGameLauncherService gameLauncher, IVnDatabaseService vnDatabaseService, IProcessMonitoringService processMonitor)
        {
            Tracker = tracker;
            _miningService = miningService;
            _configService = configService;
            _ankiHandler = ankiHandler;
            _videoEngine = videoEngine;
            _windowService = windowService;
            _sessionManager = sessionManager;
            _gameLauncher = gameLauncher;
            _vnDatabaseService = vnDatabaseService;
            _processMonitor = processMonitor;

            _processMonitor.VnProcessStarted += (s, e) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (CurrentVN != null && CurrentVN.Id != e.VisualNovel.Id)
                    {
                        return; // Ignore background VNs starting if we already have one selected
                    }

                    _ = TryAutoLinkAsync(e.VisualNovel.ProcessName);
                });
            };

            _processMonitor.VnProcessStopped += (s, e) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var config = _configService.CurrentConfig;
                    if (CurrentVN != null && CurrentVN.Id == e.VisualNovel.Id)
                    {
                        // Se o jogo ativo foi fechado
                        bool isZeroed = Tracker.ValidCharacterCount == 0 && Tracker.Elapsed.TotalSeconds == 0 && !IsBufferActive;
                        if (isZeroed)
                        {
                            config.Media.VideoWindow = string.Empty;
                            _configService.Save();
                            _miningService.TargetVideoWindow = string.Empty;
                            CurrentVN = null;
                            UpdateVisualCurrentVN();
                        }
                    }
                    else if (string.Equals(e.VisualNovel.ProcessName, config.Media.VideoWindow, System.StringComparison.OrdinalIgnoreCase))
                    {
                        // Ou se for só o "TargetVideoWindow" que não tava ativamente com session mas o cara tava usando
                        config.Media.VideoWindow = string.Empty;
                        _configService.Save();
                        _miningService.TargetVideoWindow = string.Empty;
                        CurrentVN = null;
                        UpdateVisualCurrentVN();
                    }
                });
            };

            WeakReferenceMessenger.Default.RegisterAll(this);
        }

        public async Task ApplyConfigToServices()
        {
            var config = _configService.CurrentConfig;

            // 1. Atribuições seguras de propriedades (não falham)
            _miningService.TargetVideoWindow = config.Media.VideoWindow;
            _miningService.UseDynamicTimeout = config.Session.UseDynamicTimeout;
            _miningService.MaxImageWidth = config.Media.MaxImageWidth;

            if (int.TryParse(config.Session.MaxSlots, out int parsedMax) && parsedMax > 0)
                _miningService.MaxSlots = parsedMax;

            if (double.TryParse(config.Session.IdleTime, out double parsedIdle) && parsedIdle > 0)
                _miningService.IdleTimeoutFixo = parsedIdle;

            try
            {
                _ankiHandler.UpdateSettings(config.Anki.Url, config.Anki.TimeoutSeconds);
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AnkiHandler.UpdateSettings failed: {ex.Message}");
            }

            bool isSessionActive = Tracker.ValidCharacterCount > 0 || Tracker.Elapsed.TotalSeconds > 0 || IsBufferActive;

            if (_isFirstLoad)
            {
                _isFirstLoad = false;
                Application.Current.Dispatcher.Invoke(() => UpdateVisualCurrentVN());
            }
            else if (!isSessionActive)
            {
                try
                {
                    await TryAutoLinkAsync(config.Media.VideoWindow);
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"TryAutoLinkAsync failed: {ex.Message}");
                    Application.Current.Dispatcher.Invoke(() => UpdateVisualCurrentVN());
                }
            }
            else
            {
                Application.Current.Dispatcher.Invoke(() => UpdateVisualCurrentVN());
            }
        }

        [RelayCommand]
        private void ToggleBuffer()
        {
            IsBufferActive = _sessionManager.ToggleBuffer(CurrentVN);
        }

        [RelayCommand]
        private void ToggleStats()
        {
            MiniStatsVisibility = MiniStatsVisibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        public async Task EndSessionAsync()
        {
            await _sessionManager.EndSessionAsync(CurrentVN);

            IsBufferActive = false;
            UpdateVisualCurrentVN();

            StatusText = Strings.StatusSessionEnded;
            StatusVisibility = Visibility.Visible;
        }

        public void Receive(SessionEndedMessage message)
        {
            if (message.Session != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    _windowService.OpenUserHub();
                    
                    // We need a small delay to ensure the Hub is fully initialized and registered to receive navigation
                    Task.Run(async () =>
                    {
                        await Task.Delay(200);
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            var navService = App.Current.Services.GetService(typeof(VN2Anki.Services.Interfaces.INavigationService)) as VN2Anki.Services.Interfaces.INavigationService;
                            if (navService == null) return;

                            navService.Push<VN2Anki.ViewModels.Hub.SessionDetailViewModel>(async vm => 
                            {
                                if (vm != null) await vm.InitializeAsync(message.Session);
                            });
                        });
                    });
                });
            }
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
            try
            {
                var vn = message.VisualNovel;

                // verifies if current session is active and prompts the user to confirm if they want to end it before starting a new one
                if (CurrentVN != null && (Tracker.ValidCharacterCount > 0 || Tracker.Elapsed.TotalSeconds > 0 || IsBufferActive))
                {
                    bool result = _windowService.ShowConfirmation(string.Format(Locales.Strings.MsgConfirmChangeSession, CurrentVN.Title, vn.Title), Locales.Strings.MsgAttention);

                    if (result)
                    {
                        await EndSessionAsync();
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

                WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = $"Iniciando {vn.Title}...", IsError = false }));

                var launchResult = await _gameLauncher.LaunchAndHookAsync(vn, token);

                switch (launchResult)
                {
                    case GameLaunchResult.Success:
                        WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = Locales.Strings.StatusVideoConnected, IsError = false }));
                        break;

                    case GameLaunchResult.ExecutableNotFound:
                        WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = Locales.Strings.MsgExeNotFound, IsError = true }));
                        CurrentVN = null;
                        break;

                    case GameLaunchResult.LaunchFailed:
                        WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Erro ao abrir o jogo!", IsError = true }));
                        CurrentVN = null;
                        break;

                    case GameLaunchResult.Timeout:
                        WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = Locales.Strings.MsgWindowTimeout, IsError = true }));
                        CurrentVN = null;
                        var config = _configService.CurrentConfig;
                        config.Media.VideoWindow = string.Empty;
                        _configService.Save();
                        _miningService.TargetVideoWindow = string.Empty;
                        break;

                    case GameLaunchResult.Cancelled:
                        // do nothing, silently abort
                        break;
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in Receive(PlayVnMessage): {ex.Message}");
            }
        }

        // main window vsource/vn title
        private VisualNovel? _previousVn;
        partial void OnCurrentVNChanged(VN2Anki.Models.Entities.VisualNovel? value)
        {
            // 1. Salva o estado atual no jogo anterior para não perder resizes antes de trocar
            if (_previousVn != null)
            {
                _previousVn.OverlayConfigJson = System.Text.Json.JsonSerializer.Serialize(_configService.CurrentConfig.Overlay);
                _ = _vnDatabaseService.UpdateVisualNovelAsync(_previousVn);
            }

            // 2. Carrega o estado do novo jogo
            if (value != null && !string.IsNullOrEmpty(value.OverlayConfigJson))
            {
                try
                {
                    var profile = System.Text.Json.JsonSerializer.Deserialize<OverlayConfig>(value.OverlayConfigJson);
                    if (profile != null)
                        _configService.CurrentConfig.Overlay = profile;
                }
                catch { /* Ignora e usa o global atual se falhar a leitura */ }
            }
            else
            {
                // Se o jogo novo não tem perfil, recarrega o template global do disco
                _configService.Load();
            }

            // Avisa a OverlayWindow para se redimensionar fisicamente com o novo perfil
            WeakReferenceMessenger.Default.Send(new OverlayConfigUpdatedMessage());

            _previousVn = value; // Atualiza a referência

            // Dispara as mensagens padrão do seu código
            if (value != null)
            {
                WeakReferenceMessenger.Default.Send(new CurrentVnChangedMessage(value));
            }
            else
            {
                WeakReferenceMessenger.Default.Send(new CurrentVnUnlinkedMessage());
            }

            UpdateVisualCurrentVN();
        }

        public void UpdateVisualCurrentVN()
        {
            bool isZeroed = Tracker.ValidCharacterCount == 0 && Tracker.Elapsed.TotalSeconds == 0 && !IsBufferActive;

            ManualLinkVisibility = Visibility.Visible;
            ManualLinkText = CurrentVN != null ? "-" : "+";
            ManualLinkColor = CurrentVN != null
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC3545"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#28A745"));

            var videoSource = _configService.CurrentConfig.Media.VideoWindow;
            var windows = _videoEngine.GetWindows();
            bool isProcessRunning = false;

            if (CurrentVN != null)
            {
                isProcessRunning = windows.Any(w => string.Equals(w.ProcessName, CurrentVN.ProcessName, System.StringComparison.OrdinalIgnoreCase));
                
                DisplayVnTitle = CurrentVN.Title;
                VnTitleColor = isProcessRunning 
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007ACC"))
                    : Brushes.Crimson;
            }
            else if (string.IsNullOrEmpty(videoSource))
            {
                DisplayVnTitle = "No Video Source";
                VnTitleColor = Brushes.Crimson;
                isProcessRunning = false;
            }
            else
            {
                var targetWin = windows.FirstOrDefault(w => w.ProcessName == videoSource);

                if (targetWin != null)
                {
                    DisplayVnTitle = !string.IsNullOrWhiteSpace(targetWin.Title) ? targetWin.Title : targetWin.ProcessName;
                    isProcessRunning = true;
                }
                else
                {
                    DisplayVnTitle = "No Video Source";
                    isProcessRunning = false;
                }

                VnTitleColor = Brushes.Crimson;
            }

            UpdateSemaphoreState(isProcessRunning);
        }

        private void UpdateSemaphoreState(bool isProcessRunning)
        {
            var config = _configService.CurrentConfig;
            bool hasAudio = !string.IsNullOrEmpty(config.Media.AudioDevice);

            // Video
            if (isProcessRunning)
            {
                VideoIconKind = "Monitor";
                VideoIconColor = Brushes.LimeGreen;
            }
            else
            {
                VideoIconKind = "MonitorOff";
                VideoIconColor = Brushes.Crimson;
            }

            // Audio
            if (hasAudio)
            {
                AudioIconKind = "VolumeHigh";
                AudioIconColor = Brushes.LimeGreen;
            }
            else
            {
                AudioIconKind = "VolumeOff";
                AudioIconColor = Brushes.Crimson;
            }

            // Link
            if (!isProcessRunning)
            {
                LinkIconKind = "LinkVariantOff";
                LinkIconColor = Brushes.White; // Grey background with white icon, unselectable effectively
            }
            else if (CurrentVN != null)
            {
                LinkIconKind = "LinkVariant";
                LinkIconColor = Brushes.LimeGreen;
            }
            else
            {
                LinkIconKind = "LinkVariantOff";
                LinkIconColor = Brushes.Crimson;
            }
        }

        public async Task TryAutoLinkAsync(string? specificProcessName = null)
        {
            var selectedVn = await _sessionManager.AutoSyncRunningVnAsync(specificProcessName);
            if (selectedVn != null)
            {
                CurrentVN = selectedVn;
            }
            else
            {
                var config = _configService.CurrentConfig;
                var configWin = config.Media.VideoWindow;

                if (string.IsNullOrEmpty(configWin))
                {
                   CurrentVN = null;
                }
                else 
                {
                    // Check if the process in config actually exists
                    var windows = _videoEngine.GetWindows();
                    bool exists = windows.Any(w => string.Equals(w.ProcessName, configWin, System.StringComparison.OrdinalIgnoreCase));

                    if (!exists)
                    {
                        // Ghost detected! Clear the config so it doesn't haunt the UI or services
                        config.Media.VideoWindow = string.Empty;
                        _configService.Save();
                        _miningService.TargetVideoWindow = string.Empty;
                        CurrentVN = null;
                    }
                    else if (CurrentVN != null)
                    {
                        // If we have a CurrentVN but the current config video window doesn't match its ProcessName or ExecutablePath,
                        // it means the user manually selected a different, unlinked window. We must unlink the session visually.
                        if (!string.Equals(CurrentVN.ProcessName, configWin, System.StringComparison.OrdinalIgnoreCase))
                        {
                            string exeName = !string.IsNullOrEmpty(CurrentVN.ExecutablePath) ? System.IO.Path.GetFileName(CurrentVN.ExecutablePath) : "";
                            if (string.IsNullOrEmpty(exeName) || !string.Equals(exeName, configWin, System.StringComparison.OrdinalIgnoreCase))
                            {
                                CurrentVN = null;
                            }
                        }
                    }
                }
            }
            Application.Current.Dispatcher.Invoke(() => UpdateVisualCurrentVN());
        }

        public async Task InitializeStartupAsync()
        {
            await TryAutoLinkAsync(null);
        }

        [RelayCommand]
        private async Task SelectVideoAsync()
        {
            var settings = App.Current.Services.GetRequiredService<SettingsWindow>();
            settings.ShowDialog();
            await ApplyConfigToServices();
        }

        [RelayCommand]
        private async Task SelectAudioAsync()
        {
            var settings = App.Current.Services.GetRequiredService<SettingsWindow>();
            settings.ShowDialog();
            await ApplyConfigToServices();
        }

        [RelayCommand]
        private async Task ManualLinkActionAsync()
        {
            if (IsBufferActive || Tracker.ValidCharacterCount > 0 || Tracker.Elapsed.TotalSeconds > 0)
            {
                _windowService.ShowWarning(Locales.Strings.MsgActionBlockedSession, Locales.Strings.TitleWarning);
                return;
            }

            var config = _configService.CurrentConfig;

            if (CurrentVN != null)
            {
                // Possibly prompt the user to confirm unlinking the current VN session before allowing them to link a new one manually, to prevent confusion
                WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Está VN já está linkada!", IsError = true }));
                return;
            }
            else
            {
                var videoSource = config.Media.VideoWindow;
                if (string.IsNullOrEmpty(videoSource))
                {
                    WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Selecione Fonte de Vídeo primeiro!", IsError = true }));
                    return;
                }

                var addWindow = App.Current.Services.GetRequiredService<AddVnWindow>();
                var vm = addWindow.DataContext as VN2Anki.ViewModels.Hub.AddVnViewModel;

                // context flag
                vm.IsOpenedFromLibrary = false;

                if (addWindow.ShowDialog() == true)
                {
                    var processToLink = vm.TargetProcessName;
                    if (!string.IsNullOrEmpty(processToLink))
                    {
                        await TryAutoLinkAsync(processToLink);
                    }
                }
                else
                {
                    UpdateVisualCurrentVN();
                }
            }
        }

        // checks for registered running VN processes 
        [RelayCommand]
        private async Task AutoSyncActionAsync()
        {
            // prevents if there's an ongoing session
            // avoid data loss
            if (IsBufferActive || Tracker.ValidCharacterCount > 0 || Tracker.Elapsed.TotalSeconds > 0)
            {
                _windowService.ShowWarning(Locales.Strings.MsgActionBlockedAutoSync, Locales.Strings.TitleWarning);
                return;
            }

            await TryAutoLinkAsync();
        }

        public void Receive(BufferStoppedMessage message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsBufferActive = false;
                _sessionManager.IsBufferActive = false;
            });
        }

        public void Receive(SaveOverlayStateMessage message)
        {
            // Se há um jogo rodando, salva a overlay atual no perfil dele!
            if (CurrentVN != null)
            {
                CurrentVN.OverlayConfigJson = System.Text.Json.JsonSerializer.Serialize(_configService.CurrentConfig.Overlay);
                _ = _vnDatabaseService.UpdateVisualNovelAsync(CurrentVN);
            }
        }
    }
}