using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using VN2Anki.Messages;
using VN2Anki.Models.Entities;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.ViewModels.Hub
{
    public partial class LibraryViewModel : ObservableObject
    {
        private readonly IVnDatabaseService _dbService;
        public INavigationService Navigation { get; }

        [ObservableProperty]
        private ObservableCollection<VisualNovel> _visualNovels = new();

        public LibraryViewModel(IVnDatabaseService dbService, INavigationService navigation) // <--- NOVO PARÂMETRO
        {
            _dbService = dbService;
            Navigation = navigation;
            _ = LoadLibraryAsync();
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

        [RelayCommand]
        private void GoToDetails(VisualNovel vn)
        {
            if (vn == null) return;
            Navigation.Push<VnDetailsViewModel>(vm => vm.Initialize(vn));
        }
    }
}