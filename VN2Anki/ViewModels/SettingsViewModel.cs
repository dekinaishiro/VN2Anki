using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using VN2Anki.Messages;
using VN2Anki.Models;
using VN2Anki.Services;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IConfigurationService _configService;
        private readonly IBridgeService _bridgeService;
        private readonly AudioEngine _audioEngine;
        private readonly IProcessMonitoringService _processMonitor;
        private readonly int _initialBridgePort;
        public SessionTracker Tracker { get; }

        [ObservableProperty]
        private AppConfig _config;

        [ObservableProperty]
        private ObservableCollection<AudioDeviceItem> _audioDevices = new();

        [ObservableProperty]
        private ObservableCollection<ActiveWindowItem> _videoWindows = new();

        public bool IsVideoSelectionEnabled => !Tracker.IsTracking && Tracker.ValidCharacterCount == 0 && Tracker.Elapsed.TotalSeconds == 0;
        public bool IsAudioSelectionEnabled => !Tracker.IsTracking; // Lock audio device selection if session is tracking/recording

        public SettingsViewModel(IConfigurationService configService, IBridgeService bridgeService, AudioEngine audioEngine, IProcessMonitoringService processMonitor, SessionTracker tracker)
        {
            _configService = configService;
            _bridgeService = bridgeService;
            _audioEngine = audioEngine;
            _processMonitor = processMonitor;
            Tracker = tracker;

            Config = _configService.CurrentConfig;
            _initialBridgePort = Config.Anki.YomitanBridgePort;

            Tracker.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Tracker.IsTracking) ||
                    e.PropertyName == nameof(Tracker.ValidCharacterCount) ||
                    e.PropertyName == nameof(Tracker.Elapsed))
                {
                    OnPropertyChanged(nameof(IsVideoSelectionEnabled));
                    OnPropertyChanged(nameof(IsAudioSelectionEnabled));
                }
            };
        }

        // loads the device lists when the window opens
        public async Task LoadDevicesAsync()
        {
            // loads the audio devices asynchronously to avoid freezing the UI
            var audioList = await Task.Run(() => _audioEngine.GetDevices());
            AudioDevices.Clear();
            foreach (var device in audioList)
            {
                AudioDevices.Add(device);
            }

            var videoList = await Task.Run(() => _processMonitor.GetActiveWindows());
            VideoWindows.Clear();
            foreach (var window in videoList)
            {
                VideoWindows.Add(window);
            }
        }

        [RelayCommand]
        private void Save()
        {
            _configService.Save();
            
            if (_initialBridgePort != Config.Anki.YomitanBridgePort)
            {
                _bridgeService.Restart(Config.Anki.YomitanBridgePort);
            }
        }
        public void Receive(SessionEndedMessage message)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                OnPropertyChanged(nameof(IsVideoSelectionEnabled));
                OnPropertyChanged(nameof(IsAudioSelectionEnabled));
            });
        }

    }
}