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

        public SettingsWindow(SettingsViewModel viewModel, IConfigurationService configService, AnkiHandler anki)
        {
            InitializeComponent();
            _viewModel = viewModel;
            _configService = configService;
            _anki = anki;

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
            string defaultChromePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\User Data\Default\Extensions");
            string defaultEdgePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\User Data\Default\Extensions");
            string startPath = Directory.Exists(defaultChromePath) ? defaultChromePath : (Directory.Exists(defaultEdgePath) ? defaultEdgePath : Environment.GetFolderPath(Environment.SpecialFolder.Desktop));

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
                var settingsWin = new Window { Title = "Extension Settings", Width = 900, Height = 700, WindowStartupLocation = WindowStartupLocation.CenterScreen };
                var settingsWebView = new Microsoft.Web.WebView2.Wpf.WebView2();
                settingsWin.Content = settingsWebView;
                settingsWin.Loaded += async (ss, ee) =>
                {
                    var options = new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = true };
                    var environment = await CoreWebView2Environment.CreateAsync(null, null, options);
                    await settingsWebView.EnsureCoreWebView2Async(environment);
                    try
                    {
                        string manifestPath = Path.Combine(selectedExtPath, "manifest.json");
                        string optionsHtmlPage = "options.html";
                        string targetExtName = "";
                        if (File.Exists(manifestPath))
                        {
                            var json = File.ReadAllText(manifestPath);
                            using var doc = JsonDocument.Parse(json);
                            if (doc.RootElement.TryGetProperty("name", out JsonElement nameProp)) targetExtName = nameProp.GetString();
                            if (doc.RootElement.TryGetProperty("options_ui", out JsonElement optUi) && optUi.TryGetProperty("page", out JsonElement page)) optionsHtmlPage = page.GetString();
                            else if (doc.RootElement.TryGetProperty("options_page", out JsonElement optPage)) optionsHtmlPage = optPage.GetString();
                        }
                        var loadedExtensions = await settingsWebView.CoreWebView2.Profile.GetBrowserExtensionsAsync();
                        var existingExt = loadedExtensions.FirstOrDefault(ext => ext.Name == targetExtName || (ext.Name != null && ext.Name.Contains("Yomitan")));
                        if (existingExt != null) settingsWebView.CoreWebView2.Navigate($"chrome-extension://{existingExt.Id}/{optionsHtmlPage}");
                        else
                        {
                            var newExtension = await settingsWebView.CoreWebView2.Profile.AddBrowserExtensionAsync(selectedExtPath);
                            settingsWebView.CoreWebView2.Navigate($"chrome-extension://{newExtension.Id}/{optionsHtmlPage}");
                        }
                    }
                    catch (Exception ex) { MessageBox.Show($"Could not load extension settings:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
                };
                settingsWin.Show();
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