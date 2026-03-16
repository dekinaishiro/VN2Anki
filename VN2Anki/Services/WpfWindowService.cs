using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VN2Anki.Models.Entities;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.Services
{
    public class WpfWindowService : IWindowService
    {
        private readonly IConfigurationService _configService;

        public WpfWindowService(IConfigurationService configService)
        {
            _configService = configService;
        }

        public void CloseWindow(object viewModel, bool dialogResult)
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (window.DataContext == viewModel)
                {
                    window.DialogResult = dialogResult;
                    window.Close();
                    break;
                }
            }
        }

        public VisualNovel? ShowMultipleVnPrompt(List<VisualNovel> vns)
        {
            VisualNovel? selectedVn = null;

            var win = new Window
            {
                Title = Locales.Strings.TitleMultipleVnsDetected,
                Width = 350,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252526")),
                Foreground = Brushes.White
            };
            var stack = new StackPanel { Margin = new Thickness(15) };
            stack.Children.Add(new TextBlock { Text = Locales.Strings.MsgMultipleVnsDetected, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 15) });

            var combo = new ComboBox { ItemsSource = vns, DisplayMemberPath = "Title", SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 15), Padding = new Thickness(5) };
            stack.Children.Add(combo);

            var btn = new Button { Content = Locales.Strings.BtnBindCurrentSession, Padding = new Thickness(10, 8, 10, 8), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007ACC")), Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand };
            btn.Click += (s, e) => { selectedVn = combo.SelectedItem as VisualNovel; win.DialogResult = true; win.Close(); };
            stack.Children.Add(btn);

            win.Content = stack;
            win.ShowDialog();

            return selectedVn;
        }

        public void OpenExtensionSettingsWindow(string extensionPath, object? owner = null)
        {
            if (string.IsNullOrEmpty(extensionPath) || !System.IO.Directory.Exists(extensionPath))
            {
                MessageBox.Show(Locales.Strings.MsgInvalidExtensionPath, Locales.Strings.TitleError, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var settingsWin = new Window 
            { 
                Title = "Extension Settings", 
                Width = 900, 
                Height = 700, 
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Topmost = true // Force above everything including Overlay
            };

            if (owner is Window ownerWin)
            {
                settingsWin.Owner = ownerWin;
            }

            var settingsWebView = new Microsoft.Web.WebView2.Wpf.WebView2();
            settingsWin.Content = settingsWebView;

            // Ensure WebView2 is disposed when the window closes to release file locks
            settingsWin.Closed += (s, e) =>
            {
                settingsWebView.Dispose();
            };

            settingsWin.Loaded += async (ss, ee) =>
            {
                try
                {
                    var environment = await VN2Anki.Helpers.BrowserExtensionHelper.GetSharedEnvironmentAsync();
                    await settingsWebView.EnsureCoreWebView2Async(environment);
                    
                    string manifestPath = System.IO.Path.Combine(extensionPath, "manifest.json");
                    string optionsHtmlPage = "options.html";
                    string targetExtName = "";
                    if (System.IO.File.Exists(manifestPath))
                    {
                        var json = System.IO.File.ReadAllText(manifestPath);
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("name", out System.Text.Json.JsonElement nameProp)) targetExtName = nameProp.GetString();
                        if (doc.RootElement.TryGetProperty("options_ui", out System.Text.Json.JsonElement optUi) && optUi.TryGetProperty("page", out System.Text.Json.JsonElement page)) optionsHtmlPage = page.GetString();
                        else if (doc.RootElement.TryGetProperty("options_page", out System.Text.Json.JsonElement optPage)) optionsHtmlPage = optPage.GetString();
                    }
                    var loadedExtensions = await settingsWebView.CoreWebView2.Profile.GetBrowserExtensionsAsync();
                    var existingExt = System.Linq.Enumerable.FirstOrDefault(loadedExtensions, ext => ext.Name == targetExtName || (ext.Name != null && ext.Name.Contains(targetExtName)) || (ext.Name != null && ext.Name.Contains("Yomitan")));
                    if (existingExt != null) settingsWebView.CoreWebView2.Navigate($"chrome-extension://{existingExt.Id}/{optionsHtmlPage}");
                    else
                    {
                        var newExtension = await settingsWebView.CoreWebView2.Profile.AddBrowserExtensionAsync(extensionPath);
                        settingsWebView.CoreWebView2.Navigate($"chrome-extension://{newExtension.Id}/{optionsHtmlPage}");
                    }
                }
                catch (System.Exception ex) { MessageBox.Show($"Could not load extension settings:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            };
            
            // Using ShowDialog to prevent multiple instances and force focus
            settingsWin.ShowDialog();
        }

        public void OpenExtensionsManager(object owner = null)
        {
            var manager = new ExtensionsWindow(_configService, this);
            manager.Topmost = true; // Force above everything including Overlay

            if (owner is Window ownerWin)
            {
                manager.Owner = ownerWin;
                manager.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                manager.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            // Using ShowDialog to prevent multiple instances and force focus
            manager.ShowDialog();
        }

        public void OpenUserHub()
        {
            var existingWin = Application.Current.Windows.OfType<UserHubWindow>().FirstOrDefault();
            if (existingWin != null)
            {
                if (existingWin.WindowState == WindowState.Minimized) existingWin.WindowState = WindowState.Normal;
                existingWin.Activate();
            }
            else
            {
                var hubWin = App.Current.Services.GetRequiredService<UserHubWindow>();
                hubWin.Show();
            }
        }

        public bool ShowConfirmation(string message, string title, bool isWarning = false)
        {
            var icon = isWarning ? MessageBoxImage.Warning : MessageBoxImage.Question;
            var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, icon);
            return result == MessageBoxResult.Yes;
        }

        public void ShowWarning(string message, string title)    
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public void ShowInformation(string message, string title)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}