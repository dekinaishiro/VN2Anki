using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using VN2Anki.Messages;
using VN2Anki.Services;

namespace VN2Anki.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject, IRecipient<StatusMessage>
    {
        public SessionTracker Tracker { get; }

        [ObservableProperty]
        private string _statusText = "";

        [ObservableProperty]
        private Visibility _statusVisibility = Visibility.Collapsed;

        public MainWindowViewModel(SessionTracker tracker)
        {
            Tracker = tracker;

            // "Sintoniza a rádio" para escutar mensagens de Status
            WeakReferenceMessenger.Default.Register(this);
        }

        // Método chamado automaticamente quando alguém envia uma StatusMessage
        public void Receive(StatusMessage message)
        {
            // Tira a responsabilidade do Dispatcher do MiningService e traz pra camada de UI
            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusText = message.Value;
                StatusVisibility = string.IsNullOrEmpty(message.Value) ? Visibility.Collapsed : Visibility.Visible;
            });
        }
    }
}