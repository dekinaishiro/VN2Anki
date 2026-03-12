using System;
using System.Windows;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.Windows
{
    public partial class SessionLogDebugWindow : Window
    {
        private readonly ISessionLoggerService _loggerService;

        public SessionLogDebugWindow(ISessionLoggerService loggerService)
        {
            InitializeComponent();
            _loggerService = loggerService;
            
            _loggerService.OnLogWritten += LoggerService_OnLogWritten;
            this.Closed += SessionLogDebugWindow_Closed;
        }

        private void LoggerService_OnLogWritten(object? sender, string jsonLine)
        {
            Dispatcher.InvokeAsync(() =>
            {
                LogListBox.Items.Add(jsonLine);
                if (LogListBox.Items.Count > 1000) LogListBox.Items.RemoveAt(0);
                LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
            });
        }

        private void SessionLogDebugWindow_Closed(object? sender, EventArgs e)
        {
            if (_loggerService != null)
                _loggerService.OnLogWritten -= LoggerService_OnLogWritten;
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            LogListBox.Items.Clear();
        }
    }
}
