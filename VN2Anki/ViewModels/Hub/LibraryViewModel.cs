using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using VN2Anki.Extensions;
using VN2Anki.Messages;
using VN2Anki.Models.Entities;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.ViewModels.Hub
{
    public partial class LibraryViewModel : ObservableObject, IRecipient<SessionSavedMessage>, IRecipient<VnDeletedMessage>, IRecipient<VnUpdatedMessage>
    {
        private readonly IVnDatabaseService _dbService;
        public INavigationService Navigation { get; }

        [ObservableProperty]
        private ObservableCollection<VisualNovel> _visualNovels = new();

        [ObservableProperty]
        private string _sortBy = "LastPlayed";

        [ObservableProperty]
        private bool _isAscending = false;

        [ObservableProperty]
        private bool _isEffectiveTimeMode = false;

        public LibraryViewModel(IVnDatabaseService dbService, INavigationService navigation)
        {
            _dbService = dbService;
            Navigation = navigation;
            _ = LoadLibraryAsync();

            WeakReferenceMessenger.Default.RegisterAll(this);
        }

        [RelayCommand]
        private void ToggleTimeMode()
        {
            IsEffectiveTimeMode = !IsEffectiveTimeMode;
        }

        public async Task LoadLibraryAsync()
        {
            var vns = await _dbService.GetAllVisualNovelsAsync();
            VisualNovels.UpdateFromUIThread(vns);
            SortLibrary();
        }

        [RelayCommand]
        private void ToggleSortDirection()
        {
            IsAscending = !IsAscending;
            SortLibrary();
        }

        partial void OnSortByChanged(string value) => SortLibrary();

        private void SortLibrary()
        {
            if (VisualNovels == null || !VisualNovels.Any()) return;

            IOrderedEnumerable<VisualNovel> sorted;

            if (SortBy == "LastPlayed")
            {
                sorted = IsAscending 
                    ? VisualNovels.OrderBy(v => v.LastPlayed ?? System.DateTime.MinValue) 
                    : VisualNovels.OrderByDescending(v => v.LastPlayed ?? System.DateTime.MinValue);
            }
            else
            {
                sorted = IsAscending 
                    ? VisualNovels.OrderBy(v => v.Title) 
                    : VisualNovels.OrderByDescending(v => v.Title);
            }

            var sortedList = sorted.ToList();
            VisualNovels.UpdateFromUIThread(sortedList);
        }

        [RelayCommand]
        private async Task DeleteVnAsync(VisualNovel vn)
        {
            if (vn == null) return;
            await _dbService.DeleteVisualNovelAsync(vn);
            VisualNovels.RemoveFromUIThread(vn);
        }

        [RelayCommand]
        private void PlayVn(VisualNovel vn)
        {
            if (vn == null) return;
            WeakReferenceMessenger.Default.Send(new PlayVnMessage(vn));
        }

        [RelayCommand]
        private void GoToDetails(VisualNovel vn)
        {
            if (vn == null) return;
            Navigation.Push<VnDetailsViewModel>(vm => vm.Initialize(vn));
        }

        public void Receive(SessionSavedMessage message) => _ = LoadLibraryAsync();

        public void Receive(VnDeletedMessage message)
        {
            var itemToRemove = VisualNovels.FirstOrDefault(v => v.Id == message.Value.Id);
            VisualNovels.RemoveFromUIThread(itemToRemove);
        }

        public void Receive(VnUpdatedMessage message) => _ = LoadLibraryAsync();
    }
}