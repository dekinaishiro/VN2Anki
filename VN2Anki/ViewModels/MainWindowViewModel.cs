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
using VN2Anki.Models.Entities;
using VN2Anki.Services;
using VN2Anki.Services.Interfaces;


namespace VN2Anki.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject, IRecipient<StatusMessage>, IRecipient<PlayVnMessage>, IRecipient<BufferStoppedMessage>
    {
        private readonly MiningService _miningService;
        private readonly IConfigurationService _configService;
        private readonly AnkiExportService _ankiExportService;
        private readonly AnkiHandler _ankiHandler;
        private CancellationTokenSource _pollingCts;
        private readonly DiscordRpcService _discordRpc;
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
        private VN2Anki.Models.Entities.VisualNovel _currentVN;

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

        private readonly DispatcherTimer _idleWindowCheckTimer;

        private bool _isFirstLoad = true;
        private readonly IWindowService _windowService;
        private readonly IGameLauncherService _gameLauncher;
        private readonly IVnDatabaseService _vnDatabaseService;

        public MainWindowViewModel(SessionTracker tracker, MiningService miningService, IConfigurationService configService, AnkiExportService ankiExportService, AnkiHandler ankiHandler, VideoEngine videoEngine, IWindowService windowService, ISessionManagerService sessionManager, IGameLauncherService gameLauncher, IVnDatabaseService vnDatabaseService)
        {
            Tracker = tracker;
            _miningService = miningService;
            _configService = configService;
            _ankiExportService = ankiExportService;
            _ankiHandler = ankiHandler;
            _videoEngine = videoEngine;
            _windowService = windowService;
            _sessionManager = sessionManager;
            _gameLauncher = gameLauncher;
            _vnDatabaseService = vnDatabaseService;

            _idleWindowCheckTimer = new DispatcherTimer { Interval = System.TimeSpan.FromSeconds(2) };
            _idleWindowCheckTimer.Tick += IdleWindowCheckTimer_Tick;
            _idleWindowCheckTimer.Start();

            WeakReferenceMessenger.Default.RegisterAll(this);
        }

        public async Task ApplyConfigToServices()
        {
            try
            {
                var config = _configService.CurrentConfig;
                _miningService.TargetVideoWindow = config.Media.VideoWindow;
                if (int.TryParse(config.Session.MaxSlots, out int parsedMax) && parsedMax > 0) _miningService.MaxSlots = parsedMax;
                if (double.TryParse(config.Session.IdleTime, out double parsedIdle) && parsedIdle > 0) _miningService.IdleTimeoutFixo = parsedIdle;

                _miningService.UseDynamicTimeout = config.Session.UseDynamicTimeout;
                _miningService.MaxImageWidth = config.Media.MaxImageWidth;
                _ankiHandler.UpdateSettings(config.Anki.Url, config.Anki.TimeoutSeconds);

                bool isSessionActive = Tracker.ValidCharacterCount > 0 || Tracker.Elapsed.TotalSeconds > 0 || IsBufferActive;

                if (_isFirstLoad)
                {
                    _isFirstLoad = false;
                    Application.Current.Dispatcher.Invoke(() => UpdateVisualCurrentVN());
                }
                else if (!isSessionActive)
                {
                    await TryAutoLinkAsync(config.Media.VideoWindow);
                }
                else
                {
                    Application.Current.Dispatcher.Invoke(() => UpdateVisualCurrentVN());
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ApplyConfigToServices: {ex.Message}");
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

        public async Task EndSessionAsync()
        {
            await _sessionManager.EndSessionAsync(CurrentVN);

            IsBufferActive = false;
            UpdateVisualCurrentVN();

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
        partial void OnCurrentVNChanged(VN2Anki.Models.Entities.VisualNovel value)
        {
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

            //ManualLinkVisibility = isZeroed ? Visibility.Visible : Visibility.Collapsed;
            ManualLinkVisibility = Visibility.Visible;
            ManualLinkText = CurrentVN != null ? "-" : "+";
            ManualLinkColor = CurrentVN != null
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC3545"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#28A745"));
            if (CurrentVN != null)
            {
                DisplayVnTitle = CurrentVN.Title;
                VnTitleColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007ACC"));
                return;
            }

            var videoSource = _configService.CurrentConfig.Media.VideoWindow;

            if (string.IsNullOrEmpty(videoSource))
            {
                DisplayVnTitle = "No Video Source";
                VnTitleColor = Brushes.Crimson;
                return;
            }

            var windows = _videoEngine.GetWindows();
            var targetWin = windows.FirstOrDefault(w => w.ProcessName == videoSource);

            if (targetWin != null)
            {
                DisplayVnTitle = !string.IsNullOrWhiteSpace(targetWin.Title) ? targetWin.Title : targetWin.ProcessName;
            }
            else
            {
                DisplayVnTitle = "No Video Source";
            }

            VnTitleColor = Brushes.Crimson;
        }

        public async Task TryAutoLinkAsync(string specificProcessName = null)
        {
            var selectedVn = await _sessionManager.AutoSyncRunningVnAsync(specificProcessName);
            if (selectedVn != null)
            {
                CurrentVN = selectedVn;
            }
            else
            {
                if (string.IsNullOrEmpty(_configService.CurrentConfig.Media.VideoWindow))
                {
                   CurrentVN = null;
                }
            }
            Application.Current.Dispatcher.Invoke(() => UpdateVisualCurrentVN());
        }

        public async Task InitializeStartupAsync()
        {
            var config = _configService.CurrentConfig;
            await TryAutoLinkAsync(config.Media.VideoWindow);
        }

        [RelayCommand]
        private async Task ManualLinkActionAsync()
        {
            if (IsBufferActive || Tracker.ValidCharacterCount > 0 || Tracker.Elapsed.TotalSeconds > 0)
            {
                _windowService.ShowWarning(Locales.Strings.MsgActionBlockedSession, Locales.Strings.TitleWarning);
                return;
            }

            if (CurrentVN != null)
            {
                CurrentVN = null; // safe manual unlinking

                var config = _configService.CurrentConfig;
                config.Media.VideoWindow = string.Empty;
                _configService.Save();
                _miningService.TargetVideoWindow = string.Empty;
                UpdateVisualCurrentVN();
            }
            else
            {
                var videoSource = _configService.CurrentConfig.Media.VideoWindow;

                var addWindow = App.Current.Services.GetRequiredService<AddVnWindow>();
                var vm = addWindow.DataContext as VN2Anki.ViewModels.Hub.AddVnViewModel;

                // context flag
                vm.IsOpenedFromLibrary = false;

                if (!string.IsNullOrEmpty(videoSource))
                {
                    var matchWin = vm.OpenWindows.FirstOrDefault(w => w.BaseItem.ProcessName == videoSource);
                    if (matchWin != null) vm.SelectedWindow = matchWin;
                }

                if (addWindow.ShowDialog() == true)
                {
                    var processToLink = vm.SelectedWindow?.BaseItem.ProcessName;
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

        private void IdleWindowCheckTimer_Tick(object? sender, System.EventArgs e)
        {
            bool wasDisconnected = _sessionManager.PerformIdleCheck();

            if (wasDisconnected)
            {
                CurrentVN = null;
                UpdateVisualCurrentVN();
            }
        }

        public void Receive(BufferStoppedMessage message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsBufferActive = false;
                _sessionManager.IsBufferActive = false;
            });
        }
    }
}