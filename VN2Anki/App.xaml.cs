using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using VN2Anki.Services;

namespace VN2Anki
{
    public partial class App : Application
    {
        private static Mutex _mutex = null;

        protected override void OnStartup(StartupEventArgs e)
        {
            const string appName = "VN2Anki_SingleInstance_Mutex";
            bool createdNew;

            // check if app is running
            _mutex = new Mutex(true, appName, out createdNew);

            if (!createdNew)
            {
                MessageBox.Show("VN2Anki is already running", "VN2Anki", MessageBoxButton.OK, MessageBoxImage.Information);
                Application.Current.Shutdown();
                return;
            }

            base.OnStartup(e);

            // wpf closes only if mainwindow is closed
            this.ShutdownMode = ShutdownMode.OnMainWindowClose;
            
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

        // ensure mutex is released
        protected override void OnExit(ExitEventArgs e)
        {
            if (_mutex != null)
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
            }
            base.OnExit(e);
        }
    }
}