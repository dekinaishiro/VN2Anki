using System.Windows;
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
    }
}