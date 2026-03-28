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
    public partial class MainWindowViewModel : ObservableObject, IDisposable, IRecipient<StatusMessage>, IRecipient<BufferStoppedMessage>, IRecipient<SessionEndedMessage>, IRecipient<SlotCapturedMessage>, IRecipient<SlotRemovedMessage>, IRecipient<HistoryClearedMessage>, IRecipient<CurrentVnChangedMessage>, IRecipient<CurrentVnUnlinkedMessage>, IRecipient<AppConfigChangedMessage>
    {
        private readonly IConfigurationService _configService;
        private readonly IWindowService _windowService;
        private readonly AnkiHandler _ankiHandler;
        private readonly ISessionManagerService _sessionManager;
        private readonly IDispatcherService _dispatcher;
        private readonly MiningService _miningService;

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


        public MainWindowViewModel(SessionTracker tracker, MiningService miningService, IConfigurationService configService, AnkiHandler ankiHandler, IWindowService windowService, ISessionManagerService sessionManager, IDispatcherService dispatcher)
        {
            Tracker = tracker;
            _miningService = miningService;
            _configService = configService;
            _ankiHandler = ankiHandler;
            _windowService = windowService;
            _sessionManager = sessionManager;
            _dispatcher = dispatcher;

            WeakReferenceMessenger.Default.RegisterAll(this);
            
            // Initial sync
            CurrentVN = _sessionManager.CurrentVN;
            SyncAnkiSettings();
            UpdateVisualCurrentVN();
        }

        public void Dispose()
        {
            WeakReferenceMessenger.Default.UnregisterAll(this);
        }

        private void SyncAnkiSettings()
        {
            var config = _configService.CurrentConfig;
            try
            {
                _ankiHandler.UpdateSettings(config.Anki.Url, config.Anki.TimeoutSeconds);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AnkiHandler.UpdateSettings failed: {ex.Message}");
            }
        }

        public void Receive(AppConfigChangedMessage message)
        {
            _dispatcher.Invoke(() =>
            {
                SyncAnkiSettings();
                UpdateVisualCurrentVN();
            });
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
        private void OpenHub() => _windowService.OpenUserHub();

        [RelayCommand]
        private void OpenSettings() => _windowService.OpenSettings();

        [RelayCommand]
        private void OpenOverlay() => _windowService.OpenOverlay();

        [RelayCommand]
        private void OpenHistory() => _windowService.OpenMiningHistory(MiningHistory, slot => _miningService.DeleteSlot(slot));

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
                UpdateVisualCurrentVN();
            });
        }

        public void Receive(CurrentVnUnlinkedMessage message)
        {
            _dispatcher.Invoke(() =>
            {
                CurrentVN = null;
                UpdateVisualCurrentVN();
            });
        }

        public void UpdateVisualCurrentVN()
        {
            var config = _configService.CurrentConfig;
            var videoSource = config.Media.VideoWindow;
            
            bool isProcessRunning = false;
            if (!string.IsNullOrEmpty(videoSource))
            {
                var procs = System.Diagnostics.Process.GetProcessesByName(videoSource);
                isProcessRunning = procs.Any(p => p.MainWindowHandle != IntPtr.Zero);
                foreach (var p in procs) p.Dispose();
            }

            if (CurrentVN != null)
            {
                ConnectionState.DisplayVnTitle = CurrentVN.Title;
                ConnectionState.VnTitleColor = isProcessRunning ? StateBrushes.Blue : Brushes.Crimson;
            }
            else if (string.IsNullOrEmpty(videoSource))
            {
                ConnectionState.DisplayVnTitle = "No Video Source";
                ConnectionState.VnTitleColor = Brushes.Crimson;
            }
            else
            {
                ConnectionState.DisplayVnTitle = videoSource;
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
        private void SelectVideo() => _windowService.OpenSettings();

        [RelayCommand]
        private void SelectAudio() => _windowService.OpenSettings();

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

                var sessionMgr = _sessionManager as SessionManagerService;
                if (sessionMgr != null)
                {
                    var addWindow = App.Current.Services.GetRequiredService<AddVnWindow>();
                    var vm = addWindow.DataContext as VN2Anki.ViewModels.Hub.AddVnViewModel;
                    vm.IsOpenedFromLibrary = false;

                    if (addWindow.ShowDialog() == true)
                    {
                        var processToLink = vm.TargetProcessName;
                        if (!string.IsNullOrEmpty(processToLink))
                        {
                            await sessionMgr.TryAutoLinkAsync(processToLink);
                        }
                    }
                    else
                    {
                        UpdateVisualCurrentVN();
                    }
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