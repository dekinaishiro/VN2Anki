using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using VN2Anki.Messages;
using VN2Anki.Models;
using VN2Anki.Services;

namespace VN2Anki.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IConfigurationService _configService;
        private readonly AudioEngine _audioEngine;
        private readonly VideoEngine _videoEngine;
        public SessionTracker Tracker { get; }

        [ObservableProperty]
        private AppConfig _config;

        [ObservableProperty]
        private ObservableCollection<AudioDeviceItem> _audioDevices = new();

        [ObservableProperty]
        private ObservableCollection<VideoEngine.VideoWindowItem> _videoWindows = new();

        public bool IsVideoSelectionEnabled => !Tracker.IsTracking && Tracker.ValidCharacterCount == 0 && Tracker.Elapsed.TotalSeconds == 0;
        public SettingsViewModel(IConfigurationService configService, AudioEngine audioEngine, VideoEngine videoEngine, SessionTracker tracker)
        {
            _configService = configService;
            _audioEngine = audioEngine;
            _videoEngine = videoEngine;
            Tracker = tracker;

            Config = _configService.CurrentConfig;

            Tracker.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Tracker.IsTracking) ||
                    e.PropertyName == nameof(Tracker.ValidCharacterCount) ||
                    e.PropertyName == nameof(Tracker.Elapsed))
                {
                    OnPropertyChanged(nameof(IsVideoSelectionEnabled));
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
        }
        public void Receive(SessionEndedMessage message)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                OnPropertyChanged(nameof(IsVideoSelectionEnabled));
            });
        }

    }
}