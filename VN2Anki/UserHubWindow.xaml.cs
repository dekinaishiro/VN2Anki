using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using VN2Anki.ViewModels.Hub;

namespace VN2Anki
{
    public partial class UserHubWindow : Window
    {
        public UserHubWindow(LibraryViewModel viewModel)
        {
            InitializeComponent();
            this.DataContext = viewModel;
        }
        private void BtnAddVn_Click(object sender, RoutedEventArgs e)
        {
            var addWindow = App.Current.Services.GetRequiredService<AddVnWindow>();
            addWindow.Owner = this;

            // Se a janela fechar com DialogResult = true (guardado com sucesso)
            if (addWindow.ShowDialog() == true)
            {
                var vm = this.DataContext as VN2Anki.ViewModels.Hub.LibraryViewModel;
                vm?.LoadLibrary(); // Atualiza a lista da biblioteca para mostrar o jogo recém-adicionado
            }
        }
    }

}