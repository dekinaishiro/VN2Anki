using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using VN2Anki.Locales;
using VN2Anki.Messages;
using VN2Anki.Services;
using VN2Anki.ViewModels;

namespace VN2Anki
{
    public partial class MainWindow : Window, IRecipient<ShowFlashMessage>
    {
        private readonly IConfigurationService _configService;
        private readonly MainWindowViewModel _viewModel;
        private readonly MiningService _miningService;

        private SettingsWindow? _settingsWindowInstance;
        private OverlayWindow? _overlayWindowInstance;
        private UserHubWindow? _hubWindowInstance;

        public MainWindow(MainWindowViewModel viewModel, IConfigurationService configService, MiningService miningService)
        {
            InitializeComponent();
            _viewModel = viewModel;
            _configService = configService;
            _miningService = miningService;

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
                    // Fire and forget since we can't await in Window_Closing natively
                    _ = _viewModel.EndSessionAsync();
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                }
                // If No, let it close and discard.
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await _viewModel.ApplyConfigToServices();

            var config = _configService.CurrentConfig.General;
            if (!double.IsNaN(config.MainWindowTop) && !double.IsNaN(config.MainWindowLeft))
            {
                this.Top = config.MainWindowTop;                this.Left = config.MainWindowLeft;
            }
            else
            {
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            if (config.OpenSettingsOnStartup)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    BtnOpenSettings_Click(this, new RoutedEventArgs());
                }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }

            await _viewModel.InitializeStartupAsync();
        }

        private void SaveWindowPosition()
        {
            _configService.CurrentConfig.General.MainWindowTop = this.Top;
            _configService.CurrentConfig.General.MainWindowLeft = this.Left;
            _configService.Save();
        }

        private void DragBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) this.DragMove(); }

        private async void BtnEndSession_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(Strings.MsgConfirmEndSession, Strings.MsgAttention, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                await _viewModel.EndSessionAsync();
            }
        }

        private void BtnOpenHistory_Click(object sender, RoutedEventArgs e)
            => MiningWindow.ShowWindow(_viewModel.MiningHistory,
                slot => _miningService.DeleteSlot(slot));
        private void OpenOrActivateWindow<T>(Func<T?> getInstance, Action<T?> setInstance, Action? onClosed = null, Action<T>? onCreated = null) where T : Window
        {
            var windowInstance = getInstance();
            if (windowInstance != null)
            {
                if (windowInstance.WindowState == WindowState.Minimized) windowInstance.WindowState = WindowState.Normal;
                windowInstance.Activate();
                return;
            }

            windowInstance = App.Current.Services.GetRequiredService<T>();
            setInstance(windowInstance);
            onCreated?.Invoke(windowInstance);
            
            windowInstance.Closed += (s, args) =>
            {
                setInstance(null);
                onClosed?.Invoke();
            };
            windowInstance.Show();
        }

        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e) =>
            OpenOrActivateWindow(
                () => _settingsWindowInstance,
                win => _settingsWindowInstance = win,
                onClosed: async () => 
                { 
                    _configService.Load(); 
                    await _viewModel.ApplyConfigToServices(); 
                },
                onCreated: win => win.Owner = this);

        private void BtnOpenOverlay_Click(object sender, RoutedEventArgs e) =>
            OpenOrActivateWindow(() => _overlayWindowInstance, win => _overlayWindowInstance = win);

        private void BtnOpenHub_Click(object sender, RoutedEventArgs e) =>
            OpenOrActivateWindow(() => _hubWindowInstance, win => _hubWindowInstance = win);

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