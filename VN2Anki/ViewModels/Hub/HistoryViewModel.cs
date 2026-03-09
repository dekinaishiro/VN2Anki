using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using VN2Anki.Extensions;
using VN2Anki.Messages;
using VN2Anki.Models.Entities;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.ViewModels.Hub
{
    public partial class HistoryViewModel : ObservableObject, IRecipient<SessionSavedMessage>
    {
        private readonly IVnDatabaseService _dbService;

        [ObservableProperty]
        private ObservableCollection<SessionRecord> _sessionHistory = new();

        public HistoryViewModel(IVnDatabaseService dbService)
        {
            _dbService = dbService;
            _ = LoadHistoryAsync();
            WeakReferenceMessenger.Default.Register(this);
        }

        public async Task LoadHistoryAsync()
        {
            var sessions = await _dbService.GetAllSessionsAsync();
            SessionHistory.UpdateFromUIThread(sessions);
        }

        public void Receive(SessionSavedMessage message) => _ = LoadHistoryAsync();

        [RelayCommand]
        private async Task DeleteSessionAsync(SessionRecord session)
        {
            if (session == null) return;
            await _dbService.DeleteSessionAsync(session);
            SessionHistory.RemoveFromUIThread(session);
        }
    }
}