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
    }
}