using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows;
using System.Windows.Input;
using VN2Anki.Locales;
using VN2Anki.Messages;
using VN2Anki.Services;
using VN2Anki.Services.Interfaces;
using VN2Anki.ViewModels;

namespace VN2Anki
{
    public partial class MainWindow : Window, IRecipient<ShowFlashMessage>
    {
        private readonly MainWindowViewModel _viewModel;
        private readonly ISessionManagerService _sessionManager;
        private readonly IConfigurationService _configService;

        public MainWindow(MainWindowViewModel viewModel, ISessionManagerService sessionManager, IConfigurationService configService)
        {
            InitializeComponent();
            _viewModel = viewModel;
            _sessionManager = sessionManager;
            _configService = configService;

            this.DataContext = _viewModel;
            WeakReferenceMessenger.Default.Register(this);

            this.Loaded += Window_Loaded;
            this.Closing += Window_Closing;
            this.KeyDown += MainWindow_KeyDown;
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F12)
            {
                var debugWin = App.Current.Services.GetRequiredService<VN2Anki.Windows.SessionLogDebugWindow>();
                debugWin.Show();
            }
        }

        private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveWindowPosition();

            if (_viewModel.HasUnsavedProgress)
            {
                var result = MessageBox.Show(
                    "You have an active session with unsaved progress. Do you want to end the session and save the progress before closing?",
                    "Unsaved Progress",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    _ = _viewModel.EndSessionAsync();
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                }
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {

            var config = _configService.CurrentConfig.General;
            if (!double.IsNaN(config.MainWindowTop) && !double.IsNaN(config.MainWindowLeft))
            {
                this.Top = config.MainWindowTop;
                this.Left = config.MainWindowLeft;
            }
            else
            {
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            await _sessionManager.InitializeAsync();
            _viewModel.UpdateVisualCurrentVN();
        }

        private void SaveWindowPosition()
        {
            var config = _configService.CurrentConfig.General;
            config.MainWindowTop = this.Top;
            config.MainWindowLeft = this.Left;
            _configService.Save();
        }

        private void DragBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) 
        { 
            if (e.ChangedButton == MouseButton.Left) this.DragMove(); 
        }

        private async void BtnEndSession_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(Strings.MsgConfirmEndSession, Strings.MsgAttention, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                await _viewModel.EndSessionAsync();
            }
        }

        public void Receive(ShowFlashMessage message)
        {
            Dispatcher.Invoke(() =>
            {
                double toastWidth = 250;
                double toastHeight = 40;

                double leftPos = this.Left + (this.ActualWidth - toastWidth) / 2;
                double screenMidHeight = SystemParameters.WorkArea.Height / 2;
                double windowMidY = this.Top + (this.ActualHeight / 2);

                double topPos = windowMidY > screenMidHeight
                                ? this.Top - toastHeight - 5
                                : this.Top + this.ActualHeight + 5;

                var toast = new VN2Anki.Windows.ToastWindow(message.Value.Message, message.Value.IsError, leftPos, topPos, toastWidth, toastHeight);
                toast.Show();
            });
        }

        private void BtnExitApp_Click(object sender, RoutedEventArgs e) => this.Close();
        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;
    }
}