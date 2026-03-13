using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows;
using VN2Anki.Data;
using VN2Anki.Models;
using VN2Anki.Services;
using VN2Anki.Services.Interfaces;


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
            
            // Register YomitanBridgeService as a Singleton
            services.AddHttpClient(); // Add generic IHttpClientFactory
            services.AddSingleton<YomitanBridgeService>(sp => 
            {
                var factory = sp.GetRequiredService<IHttpClientFactory>();
                return new YomitanBridgeService(
                    sp.GetRequiredService<IConfigurationService>(),
                    sp.GetRequiredService<ILogger<YomitanBridgeService>>(),
                    sp.GetRequiredService<AnkiHandler>(),
                    sp.GetRequiredService<MediaService>(),
                    sp.GetRequiredService<MiningService>(),
                    factory.CreateClient("YomitanBridge"),
                    sp.GetRequiredService<ISessionLoggerService>()
                );
            });
            services.AddSingleton<IBridgeService>(sp => sp.GetRequiredService<YomitanBridgeService>());

            services.AddSingleton<AudioEngine>();
            services.AddTransient<IAudioPlaybackService, AudioPlaybackService>();
            services.AddSingleton<VideoEngine>();

            services.AddSingleton<ClipboardHook>();
            services.AddSingleton<WebsocketHook>();
            services.AddSingleton<ITextHook, HookManager>();

            services.AddSingleton<AnkiHandler>(sp =>
            {
                var factory = sp.GetRequiredService<IHttpClientFactory>();
                return new AnkiHandler(factory.CreateClient("AnkiHandler"));
            });

            services.AddSingleton<VndbService>(sp =>
            {
                var factory = sp.GetRequiredService<IHttpClientFactory>();
                var client = factory.CreateClient("VndbService");
                client.DefaultRequestHeaders.UserAgent.ParseAdd("VN2Anki_DesktopApp/1.2");
                return new VndbService(client);
            });

            services.AddSingleton<SessionTracker>();

            services.AddSingleton<DiscordRpcService>();

            services.AddSingleton<MediaService>();

            services.AddSingleton<IProcessMonitoringService, ProcessMonitoringService>();

            services.AddSingleton<MiningService>();
            services.AddSingleton<ISessionManagerService, SessionManagerService>();
            services.AddSingleton<IGameLauncherService, GameLauncherService>();
            services.AddSingleton<IVnDatabaseService, VnDatabaseService>();
            services.AddSingleton<IExternalToolService, ExternalToolService>();
            services.AddSingleton<ISessionLoggerService, SessionLoggerService>();
            services.AddSingleton<IUserActivityService, UserActivityService>();

            services.AddTransient<VN2Anki.ViewModels.MainWindowViewModel>();
            services.AddTransient<ViewModels.SettingsViewModel>();

            services.AddTransient<MainWindow>();
            services.AddTransient<SettingsWindow>();

            services.AddTransient<OverlayWindow>();
            services.AddTransient<VN2Anki.Windows.SessionLogDebugWindow>();

            services.AddDbContext<AppDbContext>();

            services.AddTransient<ViewModels.Hub.LibraryViewModel>();
            services.AddTransient<UserHubWindow>();

            services.AddTransient<ViewModels.Hub.AddVnViewModel>();
            services.AddTransient<AddVnWindow>();

            services.AddSingleton<IDispatcherService, WpfDispatcherService>();
            services.AddSingleton<IWindowService, WpfWindowService>();

            services.AddSingleton<INavigationService, NavigationService>();
            services.AddTransient<ViewModels.Hub.UserHubViewModel>();

            services.AddTransient<ViewModels.Hub.LibraryViewModel>();
            services.AddTransient<ViewModels.Hub.HistoryViewModel>();
            services.AddTransient<ViewModels.Hub.VnDetailsViewModel>();

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

            // Background Cleanup Task for temporary media
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    string thumbsDir = Path.Combine(appData, "VN2Anki", "thumbs");
                    
                    if (Directory.Exists(thumbsDir))
                    {
                        var files = Directory.GetFiles(thumbsDir, "*.jpg");
                        foreach (var file in files)
                        {
                            try { File.Delete(file); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to delete thumbnail {file}: {ex.Message}"); }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to clean up temporary thumbs directory.");
                }
            });

            // Resolve Configuration Service
            var configService = Services.GetRequiredService<IConfigurationService>();
            string langCode = configService.CurrentConfig.General.Language;
            System.Threading.Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(langCode);

            // ensure database is created before showing the main window
            using (var scope = Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                dbContext.Database.EnsureCreated();
                //dbContext.Database.Migrate();
            }

            // Force initialization of background services
            _ = Services.GetRequiredService<DiscordRpcService>();
            _ = Services.GetRequiredService<IBridgeService>();
            _ = Services.GetRequiredService<ISessionLoggerService>();
            
            var processMonitor = Services.GetRequiredService<IProcessMonitoringService>();
            processMonitor.StartMonitoring();

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