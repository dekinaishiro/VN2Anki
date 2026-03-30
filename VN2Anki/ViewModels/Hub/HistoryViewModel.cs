using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using VN2Anki.Extensions;
using VN2Anki.Messages;
using VN2Anki.Models.Entities;
using VN2Anki.Services;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.ViewModels.Hub
{
    public partial class HistoryViewModel : ObservableObject, IRecipient<SessionSavedMessage>
    {
        private readonly IVnDatabaseService _dbService;
        private readonly ISessionAnalyticsEngine _analyticsEngine;
        public INavigationService Navigation { get; }

        [ObservableProperty]
        private ObservableCollection<SessionRecord> _sessionHistory = new();

        public HistoryViewModel(IVnDatabaseService dbService, INavigationService navigation, ISessionAnalyticsEngine analyticsEngine)
        {
            _dbService = dbService;
            Navigation = navigation;
            _analyticsEngine = analyticsEngine;
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
        private async Task ReapplyAllAnalyticsAsync()
        {
            await _analyticsEngine.ReprocessAllSessionsAsync();
            await LoadHistoryAsync();
            WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Estatísticas recalculadas com sucesso!", IsError = false }));
        }

        [RelayCommand]
        private async Task DeleteSessionAsync(SessionRecord session)
        {
            if (session == null) return;
            await _dbService.DeleteSessionAsync(session);
            SessionHistory.RemoveFromUIThread(session);
        }

        [RelayCommand]
        private void OpenSessionDetails(SessionRecord session)
        {
            if (session == null) return;
            Navigation.Push<SessionDetailViewModel>(async vm => await vm.InitializeAsync(session));
        }
    }
}