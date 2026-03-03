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

        private SettingsWindow _settingsWindowInstance;
        private OverlayWindow _overlayWindowInstance;

        public MainWindow(MainWindowViewModel viewModel, IConfigurationService configService, MiningService miningService)
        {
            InitializeComponent();
            _viewModel = viewModel;
            _configService = configService;
            _miningService = miningService;

            this.DataContext = _viewModel;
            WeakReferenceMessenger.Default.Register(this);

            this.Loaded += Window_Loaded;
            this.Closing += (s, e) => { SaveWindowPosition(); };
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _viewModel.ApplyConfigToServices();

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

            if (config.OpenSettingsOnStartup)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    BtnOpenSettings_Click(this, new RoutedEventArgs());
                }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }

            await _viewModel.CheckRunningVNsAsync();
        }

        private void SaveWindowPosition()
        {
            _configService.CurrentConfig.General.MainWindowTop = this.Top;
            _configService.CurrentConfig.General.MainWindowLeft = this.Left;
            _configService.Save();
        }

        private void DragBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) this.DragMove(); }

        private void BtnEndSession_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(Strings.MsgConfirmEndSession, Strings.MsgAttention, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                _viewModel.EndSession();
            }
        }

        private void BtnOpenHistory_Click(object sender, RoutedEventArgs e)
            => MiningWindow.ShowWindow(_miningService.HistorySlots, 
                async slot => await _viewModel.ExportSlotAsync(slot), 
                slot => _miningService.DeleteSlot(slot));

        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_settingsWindowInstance != null)
            {
                if (_settingsWindowInstance.WindowState == WindowState.Minimized) _settingsWindowInstance.WindowState = WindowState.Normal;
                _settingsWindowInstance.Activate();
                return;
            }

            _settingsWindowInstance = App.Current.Services.GetRequiredService<SettingsWindow>();
            _settingsWindowInstance.Owner = this;
            _settingsWindowInstance.Closed += (s, args) =>
            {
                _settingsWindowInstance = null;
                _configService.Load();
                _viewModel.ApplyConfigToServices();
            };
            _settingsWindowInstance.Show();
        }

        private void BtnOpenOverlay_Click(object sender, RoutedEventArgs e)
        {
            if (_overlayWindowInstance != null)
            {
                if (_overlayWindowInstance.WindowState == WindowState.Minimized) _overlayWindowInstance.WindowState = WindowState.Normal;
                _overlayWindowInstance.Activate();
                return;
            }

            _overlayWindowInstance = App.Current.Services.GetRequiredService<OverlayWindow>();
            _overlayWindowInstance.Closed += (s, args) => { _overlayWindowInstance = null; };
            _overlayWindowInstance.Show();
        }
        private UserHubWindow _hubWindowInstance;

        private void BtnOpenHub_Click(object sender, RoutedEventArgs e)
        {
            if (_hubWindowInstance != null)
            {
                if (_hubWindowInstance.WindowState == WindowState.Minimized) _hubWindowInstance.WindowState = WindowState.Normal;
                _hubWindowInstance.Activate();
                return;
            }

            _hubWindowInstance = App.Current.Services.GetRequiredService<UserHubWindow>();
            _hubWindowInstance.Closed += (s, args) => { _hubWindowInstance = null; };
            _hubWindowInstance.Show();
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

                var f = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    Topmost = true,
                    ShowInTaskbar = false,
                    Width = toastWidth,
                    Height = toastHeight,
                    Left = leftPos,
                    Top = topPos
                };

                var border = new Border
                {
                    Background = message.Value.IsError ? Brushes.Crimson : Brushes.SeaGreen,
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Child = new TextBlock
                    {
                        Text = message.Value.Message,
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.Bold,
                        FontSize = 12,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };

                f.Content = border;
                f.Show();

                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
                t.Tick += (s, args) => { f.Close(); t.Stop(); };
                t.Start();
            });
        }

        private void BtnExitApp_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;
    }
}