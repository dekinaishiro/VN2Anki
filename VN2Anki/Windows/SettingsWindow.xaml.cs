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

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using VN2Anki.Helpers;

namespace VN2Anki
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsViewModel _viewModel;
        private readonly IConfigurationService _configService;
        private readonly AnkiHandler _anki;
        private readonly VN2Anki.Services.Interfaces.IWindowService _windowService;

        private ObservableCollection<BrowserExtensionInfo> _extensions = new();

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

        public void SwitchToExtensionsTab()
        {
            TabExtensions.IsSelected = true;
        }

        private async Task InitializeDataAsync()
        {
            // 1. Aplica as cores IMEDIATAMENTE na UI para não haver atraso
            var overlayConfig = _viewModel.Config.Overlay;
            try { PickerBgColor.SelectedColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(overlayConfig.BgColor); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error converting BgColor: {ex.Message}"); }

            try { PickerFontColor.SelectedColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(overlayConfig.FontColor); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error converting FontColor: {ex.Message}"); }

            try { PickerOutlineColor.SelectedColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(overlayConfig.OutlineColor); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error converting OutlineColor: {ex.Message}"); }

            // 2. Carrega as listas pesadas (Áudio, Vídeo e comunicação com o Anki)
            await _viewModel.LoadDevicesAsync();
            await LoadAnkiDataAsync();

            // 3. Load Extensions Settings
            LoadExtensionSettings();
            RefreshExtensionsList();
        }

        private async Task LoadAnkiDataAsync()
        {
            var (version, connError) = await _anki.IsConnectedAsync();
            if (string.IsNullOrEmpty(connError))
            {
                var (decks, deckError) = await _anki.GetDecksAsync();
                ComboDeck.ItemsSource = decks;
                
                var (models, modelError) = await _anki.GetModelsAsync();
                ComboModel.ItemsSource = models;

                if (!string.IsNullOrEmpty(deckError) || !string.IsNullOrEmpty(modelError))
                {
                    _windowService.ShowWarning(deckError ?? modelError, Locales.Strings.TitleError);
                }
            }
            else
            {
                _windowService.ShowWarning(connError, Locales.Strings.TitleError);
            }
        }
        private void BtnRefreshAnki_Click(object sender, RoutedEventArgs e) => _ = LoadAnkiDataAsync();
        private void BtnRefreshAudio_Click(object sender, RoutedEventArgs e) => _ = _viewModel.LoadAudioDevicesAsync();
        private void BtnRefreshVideo_Click(object sender, RoutedEventArgs e) => _ = _viewModel.LoadVideoWindowsAsync();

        private async void ComboModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboModel.SelectedItem is string modelName)
            {
                var (fields, error) = await _anki.GetModelFieldsAsync(modelName);
                if (string.IsNullOrEmpty(error))
                {
                    ComboFieldAudio.ItemsSource = fields;
                    ComboFieldImage.ItemsSource = fields;
                }
                else
                {
                    _windowService.ShowWarning(error, Locales.Strings.TitleError);
                }
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
                    
                    // Always pull from the specific saved field when switching
                    if (tag == 1) // Luna
                    {
                        TxtWsUrl.Text = _viewModel.Config.Hook.LunaWebSocketUrl;
                    }
                    else if (tag == 2) // Textractor
                    {
                        TxtWsUrl.Text = _viewModel.Config.Hook.TextractorWebSocketUrl;
                    }
                }
            }
        }

        private void ChkOpenSettings_Checked(object sender, RoutedEventArgs e) { }

        // --- Extensions Logic ---
        
        private void LoadExtensionSettings()
        {
            var settings = _configService.CurrentConfig.Overlay.Extensions;
            ComboBrowser.SelectedValue = settings.SelectedBrowser;
            TxtCustomPath.Text = settings.CustomPath;

            foreach (var item in ComboBrowser.Items.Cast<ComboBoxItem>())
            {
                if (item.Tag.ToString() == settings.SelectedBrowser)
                {
                    ComboBrowser.SelectedItem = item;
                    break;
                }
            }
        }

        private void RefreshExtensionsList()
        {
            string path = "";
            var selectedItem = ComboBrowser.SelectedItem as ComboBoxItem;
            if (selectedItem == null) return;

            string browser = selectedItem.Tag.ToString();
            var browserPaths = BrowserExtensionHelper.GetBrowserPaths();

            if (browser == "Custom")
            {
                path = TxtCustomPath.Text;
                GridCustomPath.Visibility = Visibility.Visible;
            }
            else
            {
                GridCustomPath.Visibility = Visibility.Collapsed;
                if (browserPaths.ContainsKey(browser))
                {
                    path = browserPaths[browser];
                }
            }

            var exts = BrowserExtensionHelper.GetExtensionsFromPath(path);
            var enabledExts = _configService.CurrentConfig.Overlay.CustomExtensions;

            bool configNeedsSave = false;

            foreach (var ext in exts)
            {
                string parentDir = Directory.GetParent(ext.Path)?.FullName;

                // Procura se existe alguma extensão salva que divide a mesma pasta "Pai" (mesmo ID)
                var oldSavedPath = enabledExts.FirstOrDefault(p =>
                    Directory.GetParent(p)?.FullName.Equals(parentDir, StringComparison.OrdinalIgnoreCase) == true);

                if (oldSavedPath != null)
                {
                    ext.IsEnabled = true;

                    // Atualiza o caminho velho pelo novo na configuração automaticamente!
                    if (!oldSavedPath.Equals(ext.Path, StringComparison.OrdinalIgnoreCase))
                    {
                        enabledExts.Remove(oldSavedPath);
                        enabledExts.Add(ext.Path);
                        configNeedsSave = true;
                    }
                }
                else
                {
                    ext.IsEnabled = enabledExts.Contains(ext.Path);
                }
            }

            if (configNeedsSave)
            {
                _configService.Save();
            }

            _extensions = new ObservableCollection<BrowserExtensionInfo>(exts);
            ListExtensions.ItemsSource = _extensions;
        }

        private void ComboBrowser_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!this.IsLoaded) return;
            RefreshExtensionsList();
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Extension Folder",
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Select Folder",
                Filter = "Folders|\n"
            };

            if (dialog.ShowDialog() == true)
            {
                string folder = Path.GetDirectoryName(dialog.FileName);
                TxtCustomPath.Text = folder;
                RefreshExtensionsList();
            }
        }

        private void ToggleExtensionStatus_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is BrowserExtensionInfo info)
            {
                var enabledExts = _configService.CurrentConfig.Overlay.CustomExtensions;
                if (info.IsEnabled)
                {
                    if (!enabledExts.Contains(info.Path)) enabledExts.Add(info.Path);
                }
                else
                {
                    enabledExts.Remove(info.Path);
                }
            }
        }

        private void BtnExtensionSettings_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is BrowserExtensionInfo info)
            {
                _windowService.OpenExtensionSettingsWindow(info.Path, this);
            }
        }

        // --- Save and Apply Logic ---

        private void ApplySettings()
        {
            // Settings Window General
            var overlayConfig = _viewModel.Config.Overlay;
            overlayConfig.BgColor = PickerBgColor.SelectedColor.ToString();
            overlayConfig.FontColor = PickerFontColor.SelectedColor.ToString();
            overlayConfig.OutlineColor = PickerOutlineColor.SelectedColor.ToString();

            // Commit the current UI URL to the specific source field
            if (ComboHookType.SelectedItem is ComboBoxItem item)
            {
                int tag = int.Parse(item.Tag.ToString());
                if (tag == 1) _viewModel.Config.Hook.LunaWebSocketUrl = TxtWsUrl.Text;
                else if (tag == 2) _viewModel.Config.Hook.TextractorWebSocketUrl = TxtWsUrl.Text;

                // Also update the active URL that the WebsocketHook service actually reads
                _viewModel.Config.Hook.WebSocketUrl = TxtWsUrl.Text;
            }

            // Extensions Settings
            var extSettings = _configService.CurrentConfig.Overlay.Extensions;
            extSettings.SelectedBrowser = (ComboBrowser.SelectedItem as ComboBoxItem)?.Tag.ToString() ?? "Chrome";
            extSettings.CustomPath = TxtCustomPath.Text;

            // Call the viewmodel's Save method so Bridge and other services restart correctly
            _viewModel.SaveCommand.Execute(null);

            // Manda o MainWindowViewModel salvar isso no Banco de Dados do Jogo Atual (se houver)
            WeakReferenceMessenger.Default.Send(new SaveOverlayStateMessage());

            WeakReferenceMessenger.Default.Send(new OverlayConfigUpdatedMessage());
            WeakReferenceMessenger.Default.Send(new BrowserExtensionUpdatedMessage());
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            ApplySettings();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            ApplySettings();
            this.Close();
        }

        private void HotkeyTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            e.Handled = true; // Prevent default textbox behavior
            if (sender is TextBox txt && txt.DataContext is HotkeyItem hk)
            {
                var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
                
                // Ignore if only a modifier key is pressed (wait for actual key)
                if (key == System.Windows.Input.Key.LeftCtrl || key == System.Windows.Input.Key.RightCtrl ||
                    key == System.Windows.Input.Key.LeftAlt || key == System.Windows.Input.Key.RightAlt ||
                    key == System.Windows.Input.Key.LeftShift || key == System.Windows.Input.Key.RightShift ||
                    key == System.Windows.Input.Key.LWin || key == System.Windows.Input.Key.RWin ||
                    key == System.Windows.Input.Key.ImeProcessed)
                {
                    return; 
                }

                if (key == System.Windows.Input.Key.Escape || key == System.Windows.Input.Key.Delete)
                {
                    hk.Key = 0;
                    hk.Modifiers = 0;
                    txt.Text = "None";
                    return;
                }

                int modifiers = 0;
                if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control) modifiers |= 0x0002;
                if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) == System.Windows.Input.ModifierKeys.Shift) modifiers |= 0x0004;
                if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Alt) == System.Windows.Input.ModifierKeys.Alt) modifiers |= 0x0001;

                hk.Key = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
                hk.Modifiers = modifiers;

                string modStr = "";
                if ((modifiers & 0x0002) != 0) modStr += "Ctrl + ";
                if ((modifiers & 0x0004) != 0) modStr += "Shift + ";
                if ((modifiers & 0x0001) != 0) modStr += "Alt + ";
                
                txt.Text = modStr + key.ToString();
            }
        }
    }

    public class HotkeyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is HotkeyItem hk)
            {
                if (hk.Key == 0) return "None";
                
                string mod = "";
                if ((hk.Modifiers & 0x0002) != 0) mod += "Ctrl + "; // MOD_CONTROL
                if ((hk.Modifiers & 0x0004) != 0) mod += "Shift + "; // MOD_SHIFT
                if ((hk.Modifiers & 0x0001) != 0) mod += "Alt + "; // MOD_ALT
                
                var key = System.Windows.Input.KeyInterop.KeyFromVirtualKey(hk.Key);
                return mod + key.ToString();
            }
            return "None";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // --- Converters from ExtensionsWindow ---

    public class StatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (bool)value ? "Added" : "Not Added";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (bool)value ? new SolidColorBrush(Color.FromRgb(40, 167, 69)) : new SolidColorBrush(Color.FromRgb(63, 63, 70));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}