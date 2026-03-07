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
        private readonly IWindowService _windowService;
        public INavigationService Navigation { get; }

        [ObservableProperty]
        private VisualNovel _vn;

        [ObservableProperty]
        private ObservableCollection<SessionRecord> _recentSessions = new();

        // === ESTADOS DO MODAL DE EDIÇÃO ===
        [ObservableProperty] private bool _isEditModalOpen;
        [ObservableProperty] private string _editExecutablePath;
        [ObservableProperty] private string _editVndbId;

        public VnDetailsViewModel(IVnDatabaseService dbService, INavigationService navigation, IWindowService windowService)
        {
            _dbService = dbService;
            Navigation = navigation;
            _windowService = windowService;
        }

        public void Initialize(VisualNovel selectedVn)
        {
            Vn = selectedVn;
            _ = LoadRecentSessionsAsync();
        }

        private async Task LoadRecentSessionsAsync()
        {
            if (Vn == null) return;
            var allSessions = await _dbService.GetAllSessionsAsync();
            var vnSessions = allSessions.Where(s => s.VisualNovelId == Vn.Id).Take(5).ToList();

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                RecentSessions.Clear();
                foreach (var s in vnSessions) RecentSessions.Add(s);
            });
        }

        // === COMANDOS DE EDIÇÃO ===
        [RelayCommand]
        private void OpenEditModal()
        {
            EditExecutablePath = Vn.ExecutablePath;
            EditVndbId = Vn.VndbId;
            IsEditModalOpen = true; // Abre o modal
        }

        [RelayCommand]
        private void CloseEditModal() => IsEditModalOpen = false; // Fecha o modal

        [RelayCommand]
        private async Task SaveEditAsync()
        {
            Vn.ExecutablePath = EditExecutablePath;
            Vn.VndbId = EditVndbId;

            await _dbService.UpdateVisualNovelAsync(Vn);

            IsEditModalOpen = false;
            WeakReferenceMessenger.Default.Send(new VnUpdatedMessage(Vn)); // Avisa a Library
        }

        // === COMANDO DE SAFE DELETE ===
        [RelayCommand]
        private async Task DeleteVnAsync()
        {
            var allSessions = await _dbService.GetAllSessionsAsync();
            int count = allSessions.Count(s => s.VisualNovelId == Vn.Id);

            bool confirm = _windowService.ShowConfirmation(
                string.Format(Locales.Strings.MsgConfirmVnDelete, Vn.Title, count),
                Locales.Strings.MsgAttention,
                true);

            if (confirm)
            {
                await _dbService.DeleteVisualNovelAsync(Vn);
                WeakReferenceMessenger.Default.Send(new VnDeletedMessage(Vn)); // Avisa a Library
                Navigation.Pop(); // Volta para a aba anterior
            }
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