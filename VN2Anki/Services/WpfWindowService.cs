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

        public VisualNovel ShowMultipleVnPrompt(List<VisualNovel> vns)
        {
            VisualNovel selectedVn = null;

            var win = new Window
            {
                Title = "Múltiplas VNs Detectadas",
                Width = 350,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252526")),
                Foreground = Brushes.White
            };
            var stack = new StackPanel { Margin = new Thickness(15) };
            stack.Children.Add(new TextBlock { Text = "Múltiplas VNs em execução detectadas.\nSelecione qual deseja vincular:", Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 15) });

            var combo = new ComboBox { ItemsSource = vns, DisplayMemberPath = "Title", SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 15), Padding = new Thickness(5) };
            stack.Children.Add(combo);

            var btn = new Button { Content = "Vincular Selecionada", Padding = new Thickness(10, 8, 10, 8), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007ACC")), Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand };
            btn.Click += (s, e) => { selectedVn = combo.SelectedItem as VisualNovel; win.DialogResult = true; win.Close(); };
            stack.Children.Add(btn);

            win.Content = stack;
            win.ShowDialog();

            return selectedVn;
        }
    }
}