using System;
using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using VN2Anki.Models;
using VN2Anki.Services;

namespace VN2Anki
{
    public partial class App : Application
    {
        private static Mutex _mutex = null;

        // Exposes the DI container to the application
        public IServiceProvider Services { get; }

        // Strongly-typed singleton instance of the App
        public new static App Current => (App)Application.Current;

        public App()
        {
            Services = ConfigureServices();
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // Setup Serilog (File Logging)
            string logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VN2Anki", "Logs");
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(Path.Combine(logDirectory, "vn2anki-log-.txt"),
                              rollingInterval: RollingInterval.Day,
                              retainedFileCountLimit: 7)
                .CreateLogger();

            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddSerilog(dispose: true);
            });

            services.AddSingleton<IConfigurationService, ConfigurationService>();

            services.AddSingleton<AudioEngine>();
            services.AddSingleton<VideoEngine>();

            services.AddSingleton<ClipboardHook>();
            services.AddSingleton<WebsocketHook>();
            services.AddSingleton<ITextHook, HookManager>();

            services.AddSingleton<AnkiHandler>();
            services.AddSingleton<SessionTracker>();

            services.AddSingleton<MediaService>();
            services.AddSingleton<AnkiExportService>();

            services.AddSingleton<MiningService>();

            services.AddTransient<VN2Anki.ViewModels.MainWindowViewModel>();
            services.AddTransient<MainWindow>();
            services.AddTransient<SettingsWindow>();

            services.AddTransient<OverlayWindow>();

            return services.BuildServiceProvider();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            const string appName = "VN2Anki_SingleInstance_Mutex";
            _mutex = new Mutex(true, appName, out bool createdNew);

            if (!createdNew)
            {
                MessageBox.Show("VN2Anki is already running.", "VN2Anki", MessageBoxButton.OK, MessageBoxImage.Information);
                Application.Current.Shutdown();
                return;
            }

            base.OnStartup(e);
            this.ShutdownMode = ShutdownMode.OnMainWindowClose;

            // Resolve Configuration Service
            var configService = Services.GetRequiredService<IConfigurationService>();
            string langCode = configService.CurrentConfig.General.Language;
            System.Threading.Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(langCode);

            // Resolve and show MainWindow via DI
            var mainWindow = Services.GetRequiredService<MainWindow>();
            this.MainWindow = mainWindow;
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_mutex != null)
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
            }

            // Flush logs before exiting
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}