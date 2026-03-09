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

        public LibraryViewModel(IVnDatabaseService dbService, INavigationService navigation)
        {
            _dbService = dbService;
            Navigation = navigation;
            _ = LoadLibraryAsync();

            WeakReferenceMessenger.Default.RegisterAll(this);
        }

        public async Task LoadLibraryAsync()
        {
            var vns = await _dbService.GetAllVisualNovelsAsync();
            VisualNovels.UpdateFromUIThread(vns);
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