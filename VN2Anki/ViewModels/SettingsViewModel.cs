using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VN2Anki.Models;
using VN2Anki.Services;

namespace VN2Anki.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IConfigurationService _configService;
        private readonly AudioEngine _audioEngine;
        private readonly VideoEngine _videoEngine;

        [ObservableProperty]
        private AppConfig _config;

        [ObservableProperty]
        private ObservableCollection<AudioDeviceItem> _audioDevices = new();

        [ObservableProperty]
        private ObservableCollection<VideoEngine.VideoWindowItem> _videoWindows = new();

        public SettingsViewModel(IConfigurationService configService, AudioEngine audioEngine, VideoEngine videoEngine)
        {
            _configService = configService;
            _audioEngine = audioEngine;
            _videoEngine = videoEngine;

            // points directly to the current in-memory configuration, so changes are reflected immediately
            Config = _configService.CurrentConfig;
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
    }
}