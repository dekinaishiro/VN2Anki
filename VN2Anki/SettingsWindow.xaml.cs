using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VN2Anki.Models;
using VN2Anki.Services;
using VN2Anki.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using VN2Anki.Messages;

namespace VN2Anki
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsViewModel _viewModel;
        private readonly IConfigurationService _configService;
        private readonly AnkiHandler _anki;
        private readonly VN2Anki.Services.Interfaces.IWindowService _windowService;

        public SettingsWindow(SettingsViewModel viewModel, IConfigurationService configService, AnkiHandler anki, VN2Anki.Services.Interfaces.IWindowService windowService)
        {
            InitializeComponent();
            _viewModel = viewModel;
            _configService = configService;
            _anki = anki;
            _windowService = windowService;

            this.DataContext = _viewModel;
            this.Loaded += async (s, e) => await InitializeDataAsync();
        }

        private async Task InitializeDataAsync()
        {
            await _viewModel.LoadDevicesAsync();
            await LoadAnkiDataAsync();

            var overlayConfig = _viewModel.Config.Overlay;
            try { PickerBgColor.SelectedColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(overlayConfig.BgColor); } catch { }
            try { PickerFontColor.SelectedColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(overlayConfig.FontColor); } catch { }
            try { PickerOverlayBgColor.SelectedColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(overlayConfig.OverlayBgColor); } catch { }

            ListExtensions.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<string>(overlayConfig.CustomExtensions);
        }

        private async Task LoadAnkiDataAsync()
        {
            if (await _anki.IsConnectedAsync())
            {
                ComboDeck.ItemsSource = await _anki.GetDecksAsync();
                ComboModel.ItemsSource = await _anki.GetModelsAsync();
            }
        }
        private void BtnRefreshAnki_Click(object sender, RoutedEventArgs e) => _ = LoadAnkiDataAsync();
        private void BtnRefreshAudio_Click(object sender, RoutedEventArgs e) => _ = _viewModel.LoadDevicesAsync();
        private void BtnRefreshVideo_Click(object sender, RoutedEventArgs e) => _ = _viewModel.LoadDevicesAsync();

        private async void ComboModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboModel.SelectedItem is string modelName)
            {
                var fields = await _anki.GetModelFieldsAsync(modelName);
                ComboFieldAudio.ItemsSource = fields;
                ComboFieldImage.ItemsSource = fields;
            }
        }

        private void ComboLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!this.IsLoaded) return;
            var result = MessageBox.Show(Locales.Strings.LangRestartNow, Locales.Strings.LblLanguage, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _configService.Save();
                System.Diagnostics.Process.Start(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
                Application.Current.Shutdown();
            }
        }

        private void ComboHookType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboHookType.SelectedItem is ComboBoxItem item && TxtWsUrl != null && LblWsUrl != null)
            {
                int tag = int.Parse(item.Tag.ToString());
                if (tag == 0) // Clipboard
                {
                    TxtWsUrl.IsEnabled = false;
                    LblWsUrl.Opacity = 0.5;
                }
                else 
                {
                    TxtWsUrl.IsEnabled = true;
                    LblWsUrl.Opacity = 1.0;
                    
                    if (tag == 1) // Luna
                    {
                        TxtWsUrl.Text = "ws://localhost:2333/api/ws/text/origin";
                    }
                    else if (tag == 2) // Textractor
                    {
                        TxtWsUrl.Text = "ws://localhost:6677";
                    }
                }
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var overlayConfig = _viewModel.Config.Overlay;
            overlayConfig.BgColor = PickerBgColor.SelectedColor.ToString();
            overlayConfig.FontColor = PickerFontColor.SelectedColor.ToString();
            overlayConfig.OverlayBgColor = PickerOverlayBgColor.SelectedColor.ToString();
            overlayConfig.CustomExtensions = ListExtensions.Items.Cast<string>().ToList();

            _configService.Save();

            WeakReferenceMessenger.Default.Send(new OverlayConfigUpdatedMessage());
            this.Close();
        }

        private void BtnAddExtension_Click(object sender, RoutedEventArgs e)
        {
            var defaultPaths = VN2Anki.Helpers.BrowserExtensionHelper.GetDefaultExtensionBasePaths();
            string startPath = defaultPaths.FirstOrDefault(Directory.Exists) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Select Extension Folder", ValidateNames = false, CheckFileExists = false, CheckPathExists = true, FileName = "Select Folder", Filter = "Folders|\n", InitialDirectory = startPath };
            if (dialog.ShowDialog(this) == true)
            {
                string extPath = System.IO.Path.GetDirectoryName(dialog.FileName);
                if (!string.IsNullOrWhiteSpace(extPath) && System.IO.Directory.Exists(extPath))
                {
                    var list = ListExtensions.ItemsSource as System.Collections.ObjectModel.ObservableCollection<string>;
                    if (!list.Contains(extPath)) list.Add(extPath);
                }
            }
        }
        private void BtnExtensionSettings_Click(object sender, RoutedEventArgs e)
        {
            if (ListExtensions.SelectedItem is string selectedExtPath)
            {
                _windowService.OpenExtensionSettingsWindow(selectedExtPath);
            }
        }
        private void BtnRemoveExtension_Click(object sender, RoutedEventArgs e)
        {
            if (ListExtensions.SelectedItem is string selectedExt)
            {
                var list = ListExtensions.ItemsSource as System.Collections.ObjectModel.ObservableCollection<string>;
                list.Remove(selectedExt);
            }
        }
        private void ChkOpenSettings_Checked(object sender, RoutedEventArgs e) { }
    }
}