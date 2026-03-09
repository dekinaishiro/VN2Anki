using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace VN2Anki.Windows
{
    public class ToastWindow : Window
    {
        private readonly DispatcherTimer _timer;

        public ToastWindow(string message, bool isError, double leftPos, double topPos, double toastWidth = 250, double toastHeight = 40)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            Width = toastWidth;
            Height = toastHeight;
            Left = leftPos;
            Top = topPos;

            Content = new Border
            {
                Background = isError ? Brushes.Crimson : Brushes.SeaGreen,
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = new TextBlock
                {
                    Text = message,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
            _timer.Tick += (s, args) => 
            { 
                this.Close(); 
                _timer.Stop(); 
            };
            
            this.Loaded += (s, args) => _timer.Start();
        }
    }
}
