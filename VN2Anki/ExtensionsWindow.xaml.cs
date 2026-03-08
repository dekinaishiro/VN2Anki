using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using VN2Anki.Helpers;
using VN2Anki.Models;
using VN2Anki.Services;
using VN2Anki.Services.Interfaces;
using CommunityToolkit.Mvvm.Messaging;
using VN2Anki.Messages;

namespace VN2Anki
{
    public partial class ExtensionsWindow : Window
    {
        private readonly IConfigurationService _configService;
        private readonly IWindowService _windowService;
        private ObservableCollection<BrowserExtensionInfo> _extensions = new();

        public ExtensionsWindow(IConfigurationService configService, IWindowService windowService)
        {
            InitializeComponent();
            _configService = configService;
            _windowService = windowService;

            LoadSettings();
            RefreshList();
        }

        private void LoadSettings()
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

        private void RefreshList()
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

            foreach (var ext in exts)
            {
                ext.IsEnabled = enabledExts.Contains(ext.Path);
            }

            _extensions = new ObservableCollection<BrowserExtensionInfo>(exts);
            ListExtensions.ItemsSource = _extensions;
        }

        private void ComboBrowser_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!this.IsLoaded) return;
            RefreshList();
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
                RefreshList();
            }
        }

        private void ToggleStatus_Click(object sender, RoutedEventArgs e)
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

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is BrowserExtensionInfo info)
            {
                _windowService.OpenExtensionSettingsWindow(info.Path);
            }
        }

        private void BtnDone_Click(object sender, RoutedEventArgs e)
        {
            var settings = _configService.CurrentConfig.Overlay.Extensions;
            settings.SelectedBrowser = (ComboBrowser.SelectedItem as ComboBoxItem)?.Tag.ToString() ?? "Chrome";
            settings.CustomPath = TxtCustomPath.Text;

            _configService.Save();
            
            // Notify Overlay to sync extensions
            WeakReferenceMessenger.Default.Send(new BrowserExtensionUpdatedMessage());
            
            this.Close();
        }
    }

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
