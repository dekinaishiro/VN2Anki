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

        public void Receive(SaveOverlayStateMessage message)
        {
            if (_activeVn != null)
            {
                _activeVn.OverlayConfigJson = JsonSerializer.Serialize(_configService.CurrentConfig.Overlay);
                _ = _vnDatabaseService.UpdateVisualNovelAsync(_activeVn);
            }
        }

        private async Task SwitchProfileAsync(VisualNovel? newVn)
        {
            // 1. Save current state to the previous VN before switching
            if (_activeVn != null)
            {
                _activeVn.OverlayConfigJson = JsonSerializer.Serialize(_configService.CurrentConfig.Overlay);
                await _vnDatabaseService.UpdateVisualNovelAsync(_activeVn);
            }

            // 2. Load the state for the new VN
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
            else
            {
                // If new game has no profile, reload global template from disk
                _configService.Load();
            }

            // Update the active reference
            _activeVn = newVn;

            // Notify OverlayWindow to physically resize with the new profile
            WeakReferenceMessenger.Default.Send(new OverlayConfigUpdatedMessage());
        }

        public void Dispose()
        {
            WeakReferenceMessenger.Default.UnregisterAll(this);
        }
    }
}
