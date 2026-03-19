using System;
﻿using CommunityToolkit.Mvvm.ComponentModel;
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
using VN2Anki.Models.State;
using VN2Anki.Services;
using VN2Anki.Services.Interfaces;


namespace VN2Anki.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject, IDisposable, IRecipient<StatusMessage>, IRecipient<PlayVnMessage>, IRecipient<BufferStoppedMessage>, IRecipient<SaveOverlayStateMessage>, IRecipient<SessionEndedMessage>
    {
        private readonly MiningService _miningService;
        private readonly IConfigurationService _configService;
        private readonly AnkiHandler _ankiHandler;
        private CancellationTokenSource? _pollingCts;
        private readonly ISessionManagerService _sessionManager;

        public bool HasUnsavedProgress => _sessionManager.HasUnsavedProgress;

                private static class StateBrushes
        {
            public static readonly SolidColorBrush Crimson = CreateFrozenBrush("#DC3545");
            public static readonly SolidColorBrush Green = CreateFrozenBrush("#28A745");
            public static readonly SolidColorBrush Blue = CreateFrozenBrush("#007ACC");
            public static readonly SolidColorBrush LimeGreen = CreateFrozenBrush("#32CD32");
            public static readonly SolidColorBrush White = CreateFrozenBrush("#FFFFFF");
            
            private static SolidColorBrush CreateFrozenBrush(string hex)
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                brush.Freeze();
                return brush;
            }
        }

        public SessionTracker Tracker { get; }
        public VnConnectionState ConnectionState { get; } = new VnConnectionState();

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

        public string BufferBtnText => IsBufferActive ? "ON" : "OFF";
        public Brush BufferBtnBackground => IsBufferActive ? Brushes.Green : Brushes.Crimson;
        


        private bool _isFirstLoad = true;
        private readonly IWindowService _windowService;
        private readonly IGameLauncherService _gameLauncher;
        private readonly IVnDatabaseService _vnDatabaseService;
        private readonly IProcessMonitoringService _processMonitor;
        private readonly IDispatcherService _dispatcher;
        private readonly IVnLinkerService _linkerService;

        public MainWindowViewModel(SessionTracker tracker, MiningService miningService, IConfigurationService configService, AnkiHandler ankiHandler, VideoEngine videoEngine, IWindowService windowService, ISessionManagerService sessionManager, IGameLauncherService gameLauncher, IVnDatabaseService vnDatabaseService, IProcessMonitoringService processMonitor, IDispatcherService dispatcher, IVnLinkerService linkerService)
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
            _dispatcher = dispatcher;
            _linkerService = linkerService;

            _processMonitor.VnProcessStarted += OnVnProcessStarted;
            _processMonitor.VnProcessStopped += OnVnProcessStopped;

            WeakReferenceMessenger.Default.RegisterAll(this);
        }

        private void OnVnProcessStarted(object? s, VnProcessEventArgs e)
        {
            _dispatcher.Invoke(() =>
            {
                if (CurrentVN != null && CurrentVN.Id != e.VisualNovel.Id)
                {
                    return; // Ignore background VNs starting if we already have one selected
                }

                _ = TryAutoLinkAsync(e.VisualNovel.ProcessName);
            });
        }

        private void OnVnProcessStopped(object? s, VnProcessEventArgs e)
        {
            _dispatcher.Invoke(() =>
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
                else if (string.Equals(e.VisualNovel.ProcessName, config.Media.VideoWindow, StringComparison.OrdinalIgnoreCase))
                {
                    // Ou se for só o "TargetVideoWindow" que não tava ativamente com session mas o cara tava usando
                    config.Media.VideoWindow = string.Empty;
                    _configService.Save();
                    _miningService.TargetVideoWindow = string.Empty;
                    CurrentVN = null;
                    UpdateVisualCurrentVN();
                }
            });
        }

        public void Dispose()
        {
            _processMonitor.VnProcessStarted -= OnVnProcessStarted;
            _processMonitor.VnProcessStopped -= OnVnProcessStopped;
            _pollingCts?.Dispose();
            WeakReferenceMessenger.Default.UnregisterAll(this);
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AnkiHandler.UpdateSettings failed: {ex.Message}");
            }

            bool isSessionActive = Tracker.ValidCharacterCount > 0 || Tracker.Elapsed.TotalSeconds > 0 || IsBufferActive;

            if (_isFirstLoad)
            {
                _isFirstLoad = false;
                _dispatcher.Invoke(() => UpdateVisualCurrentVN());
            }
            else if (!isSessionActive)
            {
                try
                {
                    await TryAutoLinkAsync(config.Media.VideoWindow);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"TryAutoLinkAsync failed: {ex.Message}");
                    _dispatcher.Invoke(() => UpdateVisualCurrentVN());
                }
            }
            else
            {
                _dispatcher.Invoke(() => UpdateVisualCurrentVN());
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
                _dispatcher.Invoke(() =>
                {
                    _windowService.OpenUserHub();
                    
                    // We need a small delay to ensure the Hub is fully initialized and registered to receive navigation
                    Task.Run(async () =>
                    {
                        await Task.Delay(200);
                        _dispatcher.Invoke(() =>
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
            _dispatcher.Invoke(() =>
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in Receive(PlayVnMessage): {ex}");
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

            var videoSource = _configService.CurrentConfig.Media.VideoWindow;
            var windows = _videoEngine.GetWindows();
            bool isProcessRunning = false;

            if (CurrentVN != null)
            {
                isProcessRunning = windows.Any(w => string.Equals(w.ProcessName, CurrentVN.ProcessName, StringComparison.OrdinalIgnoreCase));
                
                ConnectionState.DisplayVnTitle = CurrentVN.Title;
                ConnectionState.VnTitleColor = isProcessRunning ? StateBrushes.Blue : Brushes.Crimson;
            }
            else if (string.IsNullOrEmpty(videoSource))
            {
                ConnectionState.DisplayVnTitle = "No Video Source";
                ConnectionState.VnTitleColor = Brushes.Crimson;
                isProcessRunning = false;
            }
            else
            {
                var targetWin = windows.FirstOrDefault(w => w.ProcessName == videoSource);

                if (targetWin != null)
                {
                    ConnectionState.DisplayVnTitle = !string.IsNullOrWhiteSpace(targetWin.Title) ? targetWin.Title : targetWin.ProcessName;
                    isProcessRunning = true;
                }
                else
                {
                    ConnectionState.DisplayVnTitle = "No Video Source";
                    isProcessRunning = false;
                }

                ConnectionState.VnTitleColor = Brushes.Crimson;
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
                ConnectionState.VideoIconKind = "Monitor";
                ConnectionState.VideoIconColor = Brushes.LimeGreen;
            }
            else
            {
                ConnectionState.VideoIconKind = "MonitorOff";
                ConnectionState.VideoIconColor = Brushes.Crimson;
            }

            // Audio
            if (hasAudio)
            {
                ConnectionState.AudioIconKind = "VolumeHigh";
                ConnectionState.AudioIconColor = Brushes.LimeGreen;
            }
            else
            {
                ConnectionState.AudioIconKind = "VolumeOff";
                ConnectionState.AudioIconColor = Brushes.Crimson;
            }

            // Link
            if (!isProcessRunning)
            {
                ConnectionState.LinkIconKind = "LinkVariantOff";
                ConnectionState.LinkIconColor = Brushes.White; // Grey background with white icon, unselectable effectively
            }
            else if (CurrentVN != null)
            {
                ConnectionState.LinkIconKind = "LinkVariant";
                ConnectionState.LinkIconColor = Brushes.LimeGreen;
            }
            else
            {
                ConnectionState.LinkIconKind = "LinkVariantOff";
                ConnectionState.LinkIconColor = Brushes.Crimson;
            }
        }

        public async Task TryAutoLinkAsync(string? specificProcessName = null)
        {
            CurrentVN = await _linkerService.TryAutoLinkAsync(CurrentVN, specificProcessName);
            _dispatcher.Invoke(() => UpdateVisualCurrentVN());
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
            _dispatcher.Invoke(() =>
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