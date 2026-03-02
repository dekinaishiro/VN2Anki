using System.Windows;
using VN2Anki.ViewModels.Hub;

namespace VN2Anki
{
    public partial class AddVnWindow : Window
    {
        public AddVnWindow(AddVnViewModel viewModel)
        {
            InitializeComponent();
            this.DataContext = viewModel;
        }
    }
}