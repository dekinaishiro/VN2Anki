using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using VN2Anki.Messages;
using VN2Anki.Models.Entities;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.ViewModels.Hub
{
    public partial class VnDetailsViewModel : ObservableObject
    {
        private readonly IVnDatabaseService _dbService;
        public INavigationService Navigation { get; }

        [ObservableProperty]
        private VisualNovel _vn;

        [ObservableProperty]
        private ObservableCollection<SessionRecord> _recentSessions = new();

        public VnDetailsViewModel(IVnDatabaseService dbService, INavigationService navigation)
        {
            _dbService = dbService;
            Navigation = navigation;
        }

        // Método chamado pelo Motor de Navegação para injetar a VN selecionada
        public void Initialize(VisualNovel selectedVn)
        {
            Vn = selectedVn;
            _ = LoadRecentSessionsAsync();
        }

        private async Task LoadRecentSessionsAsync()
        {
            if (Vn == null) return;
            var allSessions = await _dbService.GetAllSessionsAsync();
            var vnSessions = allSessions.Where(s => s.VisualNovelId == Vn.Id).Take(5).ToList(); // Pega só as 5 últimas

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                RecentSessions.Clear();
                foreach (var s in vnSessions) RecentSessions.Add(s);
            });
        }

        [RelayCommand]
        private void GoBack() => Navigation.Pop();

        [RelayCommand]
        private void PlayVn()
        {
            if (Vn != null) WeakReferenceMessenger.Default.Send(new PlayVnMessage(Vn));
        }
    }
}