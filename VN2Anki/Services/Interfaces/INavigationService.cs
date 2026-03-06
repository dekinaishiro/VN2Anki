using CommunityToolkit.Mvvm.ComponentModel;

namespace VN2Anki.Services.Interfaces
{
    public interface INavigationService
    {
        ObservableObject CurrentViewModel { get; }
        void NavigateTo<TViewModel>() where TViewModel : ObservableObject;
        void Push<TViewModel>() where TViewModel : ObservableObject;
        void Pop();
    }
}