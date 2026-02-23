using System.IO;
using System.Text.Json;
using System.Windows;
using VN2Anki.Services;

namespace VN2Anki
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // wpf closes only if mainwindow is closed
            this.ShutdownMode = ShutdownMode.OnMainWindowClose;

            // Carrega a config usando o novo Manager
            var config = Models.ConfigManager.Load();
            string langCode = config.Language ?? "en-US";

            System.Threading.Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(langCode);

            var audioEngine = new AudioEngine();
            var videoEngine = new VideoEngine();
            var clipboardMonitor = new ClipboardMonitor();
            var ankiHandler = new AnkiHandler();
            var sessionTracker = new SessionTracker();

            var miningService = new MiningService(
                audioEngine, videoEngine, clipboardMonitor, ankiHandler, sessionTracker);

            var mainWindow = new MainWindow(miningService);

            this.MainWindow = mainWindow;
            mainWindow.Show();
        }
    }
}