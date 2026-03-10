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
        private readonly VideoEngine _videoEngine;
        private readonly int _initialBridgePort;
        private readonly bool _initialBridgeEnabled;
        public SessionTracker Tracker { get; }

        [ObservableProperty]
        private AppConfig _config;

        [ObservableProperty]
        private ObservableCollection<AudioDeviceItem> _audioDevices = new();

        [ObservableProperty]
        private ObservableCollection<VideoEngine.VideoWindowItem> _videoWindows = new();

        public bool IsVideoSelectionEnabled => !Tracker.IsTracking && Tracker.ValidCharacterCount == 0 && Tracker.Elapsed.TotalSeconds == 0;
        public bool IsAudioSelectionEnabled => !Tracker.IsTracking; // Lock audio device selection if session is tracking/recording

        public SettingsViewModel(IConfigurationService configService, IBridgeService bridgeService, AudioEngine audioEngine, VideoEngine videoEngine, SessionTracker tracker)
        {
            _configService = configService;
            _bridgeService = bridgeService;
            _audioEngine = audioEngine;
            _videoEngine = videoEngine;
            Tracker = tracker;

            Config = _configService.CurrentConfig;
            _initialBridgePort = Config.Anki.YomitanBridgePort;
            _initialBridgeEnabled = Config.Anki.EnableYomitanBridge;

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

            var videoList = await Task.Run(() => _videoEngine.GetWindows());
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
            
            if (_initialBridgePort != Config.Anki.YomitanBridgePort || _initialBridgeEnabled != Config.Anki.EnableYomitanBridge)
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