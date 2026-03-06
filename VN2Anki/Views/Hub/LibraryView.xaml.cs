using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;

namespace VN2Anki.Views.Hub
{
    public partial class LibraryView : UserControl
    {
        public LibraryView()
        {
            InitializeComponent();
        }

        private void BtnAddVn_Click(object sender, RoutedEventArgs e)
        {
            var addWindow = App.Current.Services.GetRequiredService<AddVnWindow>();
            addWindow.Owner = Window.GetWindow(this);

            var vm = addWindow.DataContext as ViewModels.Hub.AddVnViewModel;
            vm.IsOpenedFromLibrary = true;

            if (addWindow.ShowDialog() == true)
            {
                var libraryVm = this.DataContext as ViewModels.Hub.LibraryViewModel;
                _ = libraryVm?.LoadLibraryAsync();
            }
        }
    }
}