using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.ViewModels.Hub
{
    public partial class UserHubViewModel : ObservableObject
    {
        public INavigationService Navigation { get; }

        public UserHubViewModel(INavigationService navigationService)
        {
            Navigation = navigationService;

            Navigation.NavigateTo<LibraryViewModel>();
        }

        [RelayCommand]
        private void GoToLibrary() => Navigation.NavigateTo<LibraryViewModel>();

         [RelayCommand]
         private void GoToHistory() => Navigation.NavigateTo<HistoryViewModel>();
    }
}