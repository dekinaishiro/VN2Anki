using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using VN2Anki.Data;
using VN2Anki.Messages;
using VN2Anki.Models.Entities;
using VN2Anki.Services.Interfaces;
// using CommunityToolkit.Mvvm.Messaging;
// using VN2Anki.Messages;

namespace VN2Anki.ViewModels.Hub
{
    public partial class LibraryViewModel : ObservableObject, IRecipient<SessionSavedMessage>
    {
        private readonly IVnDatabaseService _dbService;

        [ObservableProperty]
        private ObservableCollection<VisualNovel> _visualNovels = new();

        [ObservableProperty]
        private ObservableCollection<SessionRecord> _sessionHistory = new();

        [ObservableProperty]
        private string _newVnTitle = "";

        public LibraryViewModel(IVnDatabaseService dbService)
        {
            _dbService = dbService;
            _ = LoadLibraryAsync();
            _ = LoadHistoryAsync();
            WeakReferenceMessenger.Default.Register(this);       
        }

        public async Task LoadLibraryAsync()
        {
            var vns = await _dbService.GetAllVisualNovelsAsync();
            System.Windows.Application.Current.Dispatcher.Invoke(() => 
            {
                VisualNovels.Clear();
                foreach (var vn in vns) VisualNovels.Add(vn);
            });
        }

        [RelayCommand]
        private async Task AddVnAsync()
        {
            if (string.IsNullOrWhiteSpace(NewVnTitle)) return;   

            var vn = new VisualNovel { Title = NewVnTitle, ProcessName = "Executável" };
            await _dbService.AddVisualNovelAsync(vn);

            System.Windows.Application.Current.Dispatcher.Invoke(() => 
            {
                VisualNovels.Add(vn);
                NewVnTitle = "";
            });
        }

        [RelayCommand]
        private async Task DeleteVnAsync(VisualNovel vn)
        {
            if (vn == null) return;
            await _dbService.DeleteVisualNovelAsync(vn);
            System.Windows.Application.Current.Dispatcher.Invoke(() => VisualNovels.Remove(vn));
        }

        [RelayCommand]
        private void PlayVn(VisualNovel vn)
        {
            if (vn == null) return;

            WeakReferenceMessenger.Default.Send(new PlayVnMessage(vn));
        }

        // sessions history related methods

        public async Task LoadHistoryAsync()
        {
            var sessions = await _dbService.GetAllSessionsAsync();
            System.Windows.Application.Current.Dispatcher.Invoke(() => 
            {
                SessionHistory.Clear();
                foreach (var s in sessions) SessionHistory.Add(s);   
            });
        }
        public void Receive(SessionSavedMessage message)
        {
            _ = LoadHistoryAsync();
        }

        [RelayCommand]
        private async Task DeleteSessionAsync(SessionRecord session)        
        {
            if (session == null) return;
            await _dbService.DeleteSessionAsync(session);
            System.Windows.Application.Current.Dispatcher.Invoke(() => 
            {
                SessionHistory.Remove(session);
                // To update Visual Novels stats on UI after session deletion
                _ = LoadLibraryAsync(); 
            });
        }
    }
}