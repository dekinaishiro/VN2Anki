using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using VN2Anki.Locales;
using VN2Anki.Messages;
using VN2Anki.Models;
using VN2Anki.Models.Entities;
using VN2Anki.Models.State;
using VN2Anki.Services;
using VN2Anki.Services.Interfaces;


namespace VN2Anki.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject, IDisposable, IRecipient<StatusMessage>, IRecipient<BufferStoppedMessage>, IRecipient<SessionEndedMessage>, IRecipient<SlotCapturedMessage>, IRecipient<SlotRemovedMessage>, IRecipient<HistoryClearedMessage>, IRecipient<CurrentVnChangedMessage>, IRecipient<CurrentVnUnlinkedMessage>
    {
        private readonly MiningService _miningService;
        private readonly IConfigurationService _configService;
        private readonly AnkiHandler _ankiHandler;
        private readonly ISessionManagerService _sessionManager;
        private readonly IWindowService _windowService;
        private readonly IDispatcherService _dispatcher;
        private readonly VideoEngine _videoEngine;

        public System.Collections.ObjectModel.ObservableCollection<MiningSlot> MiningHistory { get; } = new();

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

        [ObservableProperty]
        private VisualNovel? _currentVN;

        public string BufferBtnText => IsBufferActive ? "ON" : "OFF";
        public Brush BufferBtnBackground => IsBufferActive ? Brushes.Green : Brushes.Crimson;


        public MainWindowViewModel(SessionTracker tracker, MiningService miningService, IConfigurationService configService, AnkiHandler ankiHandler, VideoEngine videoEngine, IWindowService windowService, ISessionManagerService sessionManager, IDispatcherService dispatcher)
        {
            Tracker = tracker;
            _miningService = miningService;
            _configService = configService;
            _ankiHandler = ankiHandler;
            _videoEngine = videoEngine;
            _windowService = windowService;
            _sessionManager = sessionManager;
            _dispatcher = dispatcher;

            WeakReferenceMessenger.Default.RegisterAll(this);
            
            // Initial sync
            CurrentVN = _sessionManager.CurrentVN;
            UpdateVisualCurrentVN();
        }

        public void Dispose()
        {
            WeakReferenceMessenger.Default.UnregisterAll(this);
        }

        public void ApplyConfigToServices()
        {
            // Sync any immediate non-session related config if needed
            var config = _configService.CurrentConfig;
            
            try
            {
                _ankiHandler.UpdateSettings(config.Anki.Url, config.Anki.TimeoutSeconds);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AnkiHandler.UpdateSettings failed: {ex.Message}");
            }

            _dispatcher.Invoke(() => UpdateVisualCurrentVN());
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

                    var navService = App.Current.Services.GetService(typeof(VN2Anki.Services.Interfaces.INavigationService)) as VN2Anki.Services.Interfaces.INavigationService;
                    if (navService == null) return;

                    navService.Push<VN2Anki.ViewModels.Hub.SessionDetailViewModel>(async vm =>
                    {
                        if (vm != null) await vm.InitializeAsync(message.Session);
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

        partial void OnCurrentVNChanged(VisualNovel? value)
        {
            UpdateVisualCurrentVN();
        }

        public void Receive(CurrentVnChangedMessage message)
        {
            _dispatcher.Invoke(() =>
            {
                CurrentVN = message.Value;
            });
        }

        public void Receive(CurrentVnUnlinkedMessage message)
        {
            _dispatcher.Invoke(() =>
            {
                CurrentVN = null;
            });
        }

        public void UpdateVisualCurrentVN()
        {
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

            UpdateConnectionStates(isProcessRunning);
        }

        private void UpdateConnectionStates(bool isProcessRunning)
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
                ConnectionState.LinkIconColor = Brushes.White;
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

        [RelayCommand]
        private async Task SelectVideoAsync()
        {
            var settings = App.Current.Services.GetRequiredService<SettingsWindow>();
            settings.ShowDialog();
            ApplyConfigToServices();
        }

        [RelayCommand]
        private async Task SelectAudioAsync()
        {
            var settings = App.Current.Services.GetRequiredService<SettingsWindow>();
            settings.ShowDialog();
            ApplyConfigToServices();
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
                WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Esta VN já está linkada!", IsError = true }));
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
                vm.IsOpenedFromLibrary = false;

                if (addWindow.ShowDialog() == true)
                {
                    var processToLink = vm.TargetProcessName;
                    if (!string.IsNullOrEmpty(processToLink))
                    {
                        // Trigger manual link via session manager
                        var sessionMgr = _sessionManager as SessionManagerService;
                        if (sessionMgr != null) await sessionMgr.TryAutoLinkAsync(processToLink);
                    }
                }
                else
                {
                    UpdateVisualCurrentVN();
                }
            }
        }

        public void Receive(BufferStoppedMessage message)
        {
            _dispatcher.Invoke(() =>
            {
                IsBufferActive = false;
            });
        }

        public void Receive(SlotCapturedMessage message)
        {
            _dispatcher.Invoke(() =>
            {
                MiningHistory.Insert(0, message.Value);
            });
        }

        public void Receive(SlotRemovedMessage message)
        {
            _dispatcher.Invoke(() =>
            {
                MiningHistory.Remove(message.Value);
            });
        }

        public void Receive(HistoryClearedMessage message)
        {
            _dispatcher.Invoke(() =>
            {
                MiningHistory.Clear();
            });
        }
    }
}