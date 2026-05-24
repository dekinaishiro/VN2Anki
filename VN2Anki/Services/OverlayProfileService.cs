using System;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using VN2Anki.Messages;
using VN2Anki.Models;
using VN2Anki.Models.Entities;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.Services
{
    public class OverlayProfileService : IOverlayProfileService, IDisposable, 
        IRecipient<CurrentVnChangedMessage>, 
        IRecipient<CurrentVnUnlinkedMessage>,
        IRecipient<SaveOverlayStateMessage>
    {
        private readonly IVnDatabaseService _vnDatabaseService;
        private readonly IConfigurationService _configService;
        private VisualNovel? _activeVn;
        private bool _isMpvProfileActive;

        public OverlayProfileService(IVnDatabaseService vnDatabaseService, IConfigurationService configService)
        {
            _vnDatabaseService = vnDatabaseService;
            _configService = configService;

            WeakReferenceMessenger.Default.RegisterAll(this);
        }

        public void Receive(CurrentVnChangedMessage message)
        {
            var newVn = message.Value;
            
            // Fire-and-forget the async task
            _ = SwitchProfileAsync(newVn);
        }

        public void Receive(CurrentVnUnlinkedMessage message)
        {
            // Fire-and-forget the async task
            _ = SwitchProfileAsync(null);
        }

        public async void Receive(SaveOverlayStateMessage message)
        {
            if (_activeVn != null)
            {
                var vns = await _vnDatabaseService.GetAllVisualNovelsAsync();
                var latestVn = System.Linq.Enumerable.FirstOrDefault(vns, v => v.Id == _activeVn.Id);
                if (latestVn != null)
                {
                    latestVn.OverlayConfigJson = JsonSerializer.Serialize(_configService.CurrentConfig.Overlay);
                    await _vnDatabaseService.UpdateVisualNovelAsync(latestVn);
                }
            }
            else if (_isMpvProfileActive)
            {
                _configService.CurrentConfig.MpvOverlayConfigJson = JsonSerializer.Serialize(_configService.CurrentConfig.Overlay);
                _configService.Save();
            }
        }

        private async Task SwitchProfileAsync(VisualNovel? newVn)
        {
            // 1. Save current state to the previous VN or MPV before switching
            if (_activeVn != null)
            {
                var vns = await _vnDatabaseService.GetAllVisualNovelsAsync();
                var latestVn = System.Linq.Enumerable.FirstOrDefault(vns, v => v.Id == _activeVn.Id);
                if (latestVn != null)
                {
                    latestVn.OverlayConfigJson = JsonSerializer.Serialize(_configService.CurrentConfig.Overlay);
                    await _vnDatabaseService.UpdateVisualNovelAsync(latestVn);
                }
            }
            else if (_isMpvProfileActive)
            {
                _configService.CurrentConfig.MpvOverlayConfigJson = JsonSerializer.Serialize(_configService.CurrentConfig.Overlay);
                _configService.Save();
            }

            // Determine if the new state is MPV
            bool isMpvNext = newVn == null && string.Equals(_configService.CurrentConfig.Media.VideoWindow, "mpv", StringComparison.OrdinalIgnoreCase);

            // 2. Load the state for the new VN or MPV
            if (newVn != null && !string.IsNullOrEmpty(newVn.OverlayConfigJson))
            {
                try
                {
                    var profile = JsonSerializer.Deserialize<OverlayConfig>(newVn.OverlayConfigJson);
                    if (profile != null)
                        _configService.CurrentConfig.Overlay = profile;
                }
                catch { /* Ignore and use current global if parsing fails */ }
            }
            else if (isMpvNext && !string.IsNullOrEmpty(_configService.CurrentConfig.MpvOverlayConfigJson))
            {
                try
                {
                    var profile = JsonSerializer.Deserialize<OverlayConfig>(_configService.CurrentConfig.MpvOverlayConfigJson);
                    if (profile != null)
                        _configService.CurrentConfig.Overlay = profile;
                }
                catch { /* Ignore */ }
            }
            else
            {
                // If new game/mode has no profile, reload global template from disk
                _configService.Load();
            }

            // Update the active references
            _activeVn = newVn;
            _isMpvProfileActive = isMpvNext;

            // Notify OverlayWindow to physically resize with the new profile
            WeakReferenceMessenger.Default.Send(new OverlayConfigUpdatedMessage());
        }

        public void Dispose()
        {
            WeakReferenceMessenger.Default.UnregisterAll(this);
        }
    }
}
