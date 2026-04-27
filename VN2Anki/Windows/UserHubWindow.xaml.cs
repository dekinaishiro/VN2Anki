using System.Windows;
using System.Windows.Input;
using VN2Anki.ViewModels.Hub;

namespace VN2Anki
{
    public partial class UserHubWindow : Window
    {
        public UserHubWindow(UserHubViewModel viewModel)
        {
            InitializeComponent();
            this.DataContext = viewModel;
        }

        private void UserHubWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.NewSize.Width < 800)
            {
                VisualStateManager.GoToElementState(RootGrid, "Compact", true);
            }
            else
            {
                VisualStateManager.GoToElementState(RootGrid, "Expanded", true);
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F8)
            {
                if (this.DataContext is UserHubViewModel vm && vm.Navigation.CurrentViewModel is SessionDetailViewModel sessionVm)
                {
                    sessionVm.ToggleAdvancedView();
                }
            }
        }
    }
}