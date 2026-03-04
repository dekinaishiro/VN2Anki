using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using VN2Anki.Data;
using VN2Anki.Locales;
using VN2Anki.Messages;
using VN2Anki.Models.Entities;
using VN2Anki.Services;
using VN2Anki.Services.Interfaces;


namespace VN2Anki.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject, IRecipient<StatusMessage>, IRecipient<PlayVnMessage>, IRecipient<BufferStoppedMessage>
    {
        private readonly MiningService _miningService;
        private readonly IConfigurationService _configService;
        private readonly AnkiExportService _ankiExportService;
        private readonly AnkiHandler _ankiHandler;
        private CancellationTokenSource _pollingCts;
        private readonly DiscordRpcService _discordRpc;
        private readonly ISessionManagerService _sessionManager;

        public SessionTracker Tracker { get; }

        [ObservableProperty]
        private string _statusText = "";

        [ObservableProperty]
        private Visibility _statusVisibility = Visibility.Collapsed;

        [ObservableProperty]
        private Visibility _miniStatsVisibility = Visibility.Visible;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(BufferBtnText))]
        [NotifyPropertyChangedFor(nameof(BufferBtnBackground))]
        private bool _isBufferActive;

        private readonly VideoEngine _videoEngine;

        [ObservableProperty]
        private VN2Anki.Models.Entities.VisualNovel _currentVN;

        [ObservableProperty]
        private Visibility _manualLinkVisibility = Visibility.Collapsed;
        public string BufferBtnText => IsBufferActive ? "ON" : "OFF";
        public Brush BufferBtnBackground => IsBufferActive ? Brushes.Green : Brushes.Crimson;
        
        // main window title
        [ObservableProperty]
        private Brush _vnTitleColor = Brushes.Crimson;
        [ObservableProperty]
        private string _displayVnTitle = "No Video Source";
        // manual sync vn
        [ObservableProperty]
        private string _manualLinkText = "+";

        [ObservableProperty]
        private Brush _manualLinkColor = Brushes.Teal;

        private readonly DispatcherTimer _idleWindowCheckTimer;

        private bool _isFirstLoad = true;
        private readonly IWindowService _windowService;
        private readonly IGameLauncherService _gameLauncher;

        public MainWindowViewModel(SessionTracker tracker, MiningService miningService, IConfigurationService configService, AnkiExportService ankiExportService, AnkiHandler ankiHandler, VideoEngine videoEngine, DiscordRpcService discordRpc, IWindowService windowService, ISessionManagerService sessionManager, IGameLauncherService gameLauncher)
        {
            Tracker = tracker;
            _miningService = miningService;
            _configService = configService;
            _ankiExportService = ankiExportService;
            _ankiHandler = ankiHandler;
            _videoEngine = videoEngine;
            _discordRpc = discordRpc;
            _windowService = windowService;
            _sessionManager = sessionManager;
            _gameLauncher = gameLauncher;

            _idleWindowCheckTimer = new DispatcherTimer { Interval = System.TimeSpan.FromSeconds(2) };
            _idleWindowCheckTimer.Tick += IdleWindowCheckTimer_Tick;
            _idleWindowCheckTimer.Start();

            WeakReferenceMessenger.Default.RegisterAll(this);

            Tracker.PropertyChanged += Tracker_PropertyChanged;
        }

        public void ApplyConfigToServices()
        {
            var config = _configService.CurrentConfig;
            _miningService.TargetVideoWindow = config.Media.VideoWindow;
            if (int.TryParse(config.Session.MaxSlots, out int parsedMax) && parsedMax > 0) _miningService.MaxSlots = parsedMax;
            if (double.TryParse(config.Session.IdleTime, out double parsedIdle) && parsedIdle > 0) _miningService.IdleTimeoutFixo = parsedIdle;

            _miningService.UseDynamicTimeout = config.Session.UseDynamicTimeout;
            _miningService.MaxImageWidth = config.Media.MaxImageWidth;
            _ankiHandler.UpdateSettings(config.Anki.Url, config.Anki.TimeoutSeconds);

            bool isSessionActive = Tracker.ValidCharacterCount > 0 || Tracker.Elapsed.TotalSeconds > 0 || IsBufferActive;

            if (_isFirstLoad)
            {
                _isFirstLoad = false;
                Application.Current.Dispatcher.Invoke(() => UpdateVisualCurrentVN());
            }
            else if (!isSessionActive)
            {
                _ = CheckAndLinkRunningVNsAsync(config.Media.VideoWindow);
            }
            else
            {
                Application.Current.Dispatcher.Invoke(() => UpdateVisualCurrentVN());
            }
        }

        [RelayCommand]
        private void ToggleBuffer()
        {
            IsBufferActive = _sessionManager.ToggleBuffer(CurrentVN);
        }

        [RelayCommand]
        private void ToggleStats()
        {
            MiniStatsVisibility = MiniStatsVisibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        [RelayCommand]
        private async Task MiniQuickAddAsync()
        {
            if (_miningService.HistorySlots.Count == 0)
            {
                WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = Strings.MsgEmpty, IsError = true }));
                return;
            }

            var config = _configService.CurrentConfig;
            if (string.IsNullOrEmpty(config.Anki.Deck))
            {
                WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Please configure the Deck first!", IsError = true }));
                return;
            }

            var slot = _miningService.HistorySlots[0];
            var result = await _ankiExportService.ExportSlotAsync(slot, config.Anki, config.Media);

            WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload
            {
                Message = result.success ? "Card Updated" : "Error Updating",
                IsError = !result.success
            }));
        }

        public async Task ExportSlotAsync(Models.MiningSlot slot)
        {
            if (slot == null) return;

            var config = _configService.CurrentConfig;
            if (string.IsNullOrEmpty(config.Anki.Deck))
            {
                WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Please configure the Deck first!", IsError = true }));
                return;
            }

            var result = await _ankiExportService.ExportSlotAsync(slot, config.Anki, config.Media);

            WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload
            {
                Message = result.success ? "Card Updated" : "Error Updating",
                IsError = !result.success
            }));
        }

        public void EndSession()
        {
            _sessionManager.EndSession(CurrentVN);

            IsBufferActive = false;
            UpdateVisualCurrentVN();

            StatusText = Strings.StatusSessionEnded;
            StatusVisibility = Visibility.Visible;
        }

        public void Receive(StatusMessage message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusText = message.Value;
                StatusVisibility = string.IsNullOrEmpty(message.Value) ? Visibility.Collapsed : Visibility.Visible;
            });
        }

        public async void Receive(PlayVnMessage message)
        {
            var vn = message.VisualNovel;

            // verifies if current session is active and prompts the user to confirm if they want to end it before starting a new one
            if (CurrentVN != null && (Tracker.ValidCharacterCount > 0 || Tracker.Elapsed.TotalSeconds > 0 || IsBufferActive))
            {
                bool result = _windowService.ShowConfirmation($"Uma sessão está ativa com '{CurrentVN.Title}'.\nDeseja encerrá-la e iniciar outra com '{vn.Title}'?", "Atenção");

                if (result)
                {
                    EndSession();
                }
                else
                {
                    return;
                }
            }

            // polling duplicate prevention: cancels any existing polling loop before starting a new one
            _pollingCts?.Cancel();
            _pollingCts = new CancellationTokenSource();
            var token = _pollingCts.Token;

            CurrentVN = vn;

            if (IsBufferActive) ToggleBuffer();

            WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = $"Iniciando {vn.Title}...", IsError = false }));

            var launchResult = await _gameLauncher.LaunchAndHookAsync(vn, token);

            switch (launchResult)
            {
                case GameLaunchResult.Success:
                    WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Vídeo conectado! Pronto para o Play.", IsError = false }));
                    break;

                case GameLaunchResult.ExecutableNotFound:
                    WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Executável não encontrado!", IsError = true }));
                    CurrentVN = null;
                    break;

                case GameLaunchResult.LaunchFailed:
                    WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Erro ao abrir o jogo!", IsError = true }));
                    CurrentVN = null;
                    break;

                case GameLaunchResult.Timeout:
                    WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Timeout: A janela não apareceu.", IsError = true }));
                    CurrentVN = null;
                    var config = _configService.CurrentConfig;
                    config.Media.VideoWindow = string.Empty;
                    _configService.Save();
                    _miningService.TargetVideoWindow = string.Empty;
                    break;

                case GameLaunchResult.Cancelled:
                    // do nothing, silently abort
                    break;
            }
        }

        private void Tracker_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Tracker.ValidCharacterCount) && IsBufferActive)
            {
                string vnTitle = CurrentVN?.Title ?? "Reading a VN";
                string imageUrl = CurrentVN?.CoverImageUrl ?? "default_icon";

                System.DateTime startTime = System.DateTime.UtcNow.Subtract(Tracker.Elapsed);

                _ = _discordRpc.UpdatePresenceAsync(
                vnTitle, 
                "Reading", 
                $"{Tracker.ValidCharacterCount} chars", 
                startTime, 
                imageUrl
                );
            }
        }
        public async Task CheckAndLinkRunningVNsAsync(string specificProcessName = null)
        {
            if (IsBufferActive || Tracker.ValidCharacterCount > 0 || Tracker.Elapsed.TotalSeconds > 0)
            {
                Application.Current.Dispatcher.Invoke(() => UpdateVisualCurrentVN());
                return;
            }

            await Task.Delay(1000);

            List<VN2Anki.Models.Entities.VisualNovel> vnsDb;
            using (var scope = App.Current.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<VN2Anki.Data.AppDbContext>();
                vnsDb = db.VisualNovels.ToList();
            }

            if (vnsDb.Count == 0)
            {
                Application.Current.Dispatcher.Invoke(() => UpdateVisualCurrentVN());
                return;
            }

            var runningWindows = _videoEngine.GetWindows();
            var matchedVns = new System.Collections.Generic.List<VN2Anki.Models.Entities.VisualNovel>();

            var windowsToCheck = string.IsNullOrEmpty(specificProcessName)
                ? runningWindows
                : runningWindows.Where(w => w.ProcessName == specificProcessName).ToList();

            foreach (var win in windowsToCheck)
            {
                var match = vnsDb.FirstOrDefault(v =>
                    (!string.IsNullOrEmpty(v.ExecutablePath) && !string.IsNullOrEmpty(win.ExecutablePath) && v.ExecutablePath == win.ExecutablePath) ||
                    (!string.IsNullOrEmpty(v.ProcessName) && v.ProcessName == win.ProcessName));

                if (match != null && !matchedVns.Any(v => v.Id == match.Id))
                {
                    matchedVns.Add(match);
                }
            }

            // if VN is already linked but process isn't running, keep it linked but show "No Video Source" with red color and + button to allow manual 
            if (matchedVns.Count == 0)
            {
                if (!string.IsNullOrEmpty(specificProcessName)) CurrentVN = null;
                Application.Current.Dispatcher.Invoke(() => UpdateVisualCurrentVN());
                return;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                VN2Anki.Models.Entities.VisualNovel selectedVn = null;

                if (matchedVns.Count == 1)
                {
                    if (string.IsNullOrEmpty(specificProcessName))
                    {
                        bool result = _windowService.ShowConfirmation(
                            $"Detectamos que '{matchedVns[0].Title}' está em execução.\nDeseja vinculá-la e iniciar a sessão agora?",
                            "Visual Novel Detectada");

                        if (result) selectedVn = matchedVns[0];
                    }
                    else
                    {
                        selectedVn = matchedVns[0];
                    }
                }
                else
                {
                    selectedVn = _windowService.ShowMultipleVnPrompt(matchedVns);
                }

                if (selectedVn != null)
                {
                    CurrentVN = selectedVn;

                    var targetWin = runningWindows.First(w => w.ExecutablePath == selectedVn.ExecutablePath || w.ProcessName == selectedVn.ProcessName);
                    var config = _configService.CurrentConfig;
                    config.Media.VideoWindow = targetWin.ProcessName;
                    _configService.Save();

                    _miningService.TargetVideoWindow = targetWin.ProcessName;
                    if (string.IsNullOrEmpty(specificProcessName))
                        WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = $"Sessão vinculada: {selectedVn.Title}", IsError = false }));
                }
                else
                {
                    // FIX: O usuário clicou em "NÃO" ou fechou a janela de múltiplas VNs.
                    // Se o processo salvo na config for o mesmo da VN rejeitada, nós esvaziamos a Video Source.
                    var config = _configService.CurrentConfig;
                    var savedProcess = config.Media.VideoWindow;

                    if (!string.IsNullOrEmpty(savedProcess) && matchedVns.Any(v => v.ProcessName == savedProcess))
                    {
                        config.Media.VideoWindow = string.Empty;
                        _configService.Save();
                        _miningService.TargetVideoWindow = string.Empty;
                    }
                }

                UpdateVisualCurrentVN();
            });
        }

        // main window vsource/vn title
        partial void OnCurrentVNChanged(VN2Anki.Models.Entities.VisualNovel value)
        {
            UpdateVisualCurrentVN();
        }

        public void UpdateVisualCurrentVN()
        {
            bool isZeroed = Tracker.ValidCharacterCount == 0 && Tracker.Elapsed.TotalSeconds == 0 && !IsBufferActive;

            //ManualLinkVisibility = isZeroed ? Visibility.Visible : Visibility.Collapsed;
            ManualLinkVisibility = Visibility.Visible;
            ManualLinkText = CurrentVN != null ? "-" : "+";
            ManualLinkColor = CurrentVN != null
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC3545"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#28A745"));
            if (CurrentVN != null)
            {
                DisplayVnTitle = CurrentVN.Title;
                VnTitleColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007ACC"));
                return;
            }

            var videoSource = _configService.CurrentConfig.Media.VideoWindow;

            if (string.IsNullOrEmpty(videoSource))
            {
                DisplayVnTitle = "No Video Source";
                VnTitleColor = Brushes.Crimson;
                return;
            }

            var windows = _videoEngine.GetWindows();
            var targetWin = windows.FirstOrDefault(w => w.ProcessName == videoSource);

            if (targetWin != null)
            {
                DisplayVnTitle = !string.IsNullOrWhiteSpace(targetWin.Title) ? targetWin.Title : targetWin.ProcessName;
            }
            else
            {
                DisplayVnTitle = "No Video Source";
            }

            VnTitleColor = Brushes.Crimson;
        }

        [RelayCommand]
        private void ManualLinkAction()
        {
            if (IsBufferActive || Tracker.ValidCharacterCount > 0 || Tracker.Elapsed.TotalSeconds > 0)
            {
                _windowService.ShowWarning("Você não pode alterar ou desvincular a Visual Novel com uma sessão em andamento.\nFinalize a sessão atual primeiro clicando em END.", "Ação Bloqueada");
                return;
            }

            if (CurrentVN != null)
            {
                CurrentVN = null; // safe manual unlinking

                var config = _configService.CurrentConfig;
                config.Media.VideoWindow = string.Empty;
                _configService.Save();
                _miningService.TargetVideoWindow = string.Empty;
            }
            else
            {
                var videoSource = _configService.CurrentConfig.Media.VideoWindow;

                var addWindow = App.Current.Services.GetRequiredService<AddVnWindow>();
                var vm = addWindow.DataContext as VN2Anki.ViewModels.Hub.AddVnViewModel;

                // context flag
                vm.IsOpenedFromLibrary = false;

                if (!string.IsNullOrEmpty(videoSource))
                {
                    var matchWin = vm.OpenWindows.FirstOrDefault(w => w.BaseItem.ProcessName == videoSource);
                    if (matchWin != null) vm.SelectedWindow = matchWin;
                }

                if (addWindow.ShowDialog() == true)
                {
                    var processToLink = vm.SelectedWindow?.BaseItem.ProcessName;
                    if (!string.IsNullOrEmpty(processToLink))
                    {
                        _ = CheckAndLinkRunningVNsAsync(processToLink);
                    }
                }
            }
            UpdateVisualCurrentVN(); ;
        }

        // checks for registered running VN processes 
        [RelayCommand]
        private async Task AutoSyncActionAsync()
        {
            // prevents if there's an ongoing session
            // avoid data loss
            if (IsBufferActive || Tracker.ValidCharacterCount > 0 || Tracker.Elapsed.TotalSeconds > 0)
            {
                _windowService.ShowWarning("Você não pode realizar o Auto-Sync com uma sessão em andamento.\nFinalize a sessão atual primeiro clicando em END.", "Ação Bloqueada");
                return;
            }

            await CheckAndLinkRunningVNsAsync();
        }

        private void IdleWindowCheckTimer_Tick(object? sender, System.EventArgs e)
        {
            // verify if session has progress
            bool isZeroed = Tracker.ValidCharacterCount == 0 && Tracker.Elapsed.TotalSeconds == 0 && !IsBufferActive;

            if (!isZeroed) return;

            // if no progress, checks if the configured video source is still running
            // if not, resets the config and UI
            var videoSource = _configService.CurrentConfig.Media.VideoWindow;
            if (string.IsNullOrEmpty(videoSource)) return; 

            var procs = System.Diagnostics.Process.GetProcessesByName(videoSource);
            bool isRunning = false;
            foreach (var p in procs)
            {
                if (p.MainWindowHandle != System.IntPtr.Zero) isRunning = true;
                p.Dispose();
            }

            // process closed while app was idle = reset video source, unlink
            if (!isRunning)
            {
                CurrentVN = null;

                var config = _configService.CurrentConfig;
                config.Media.VideoWindow = string.Empty;
                _configService.Save();

                _miningService.TargetVideoWindow = string.Empty;

                UpdateVisualCurrentVN();
            }
        }

        public void Receive(BufferStoppedMessage message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsBufferActive = false;
                _sessionManager.IsBufferActive = false;
            });

            string vnTitle = CurrentVN?.Title ?? "VN2Anki";
            string imageUrl = CurrentVN?.CoverImageUrl ?? "default_icon";
            string elapsedStr = Tracker.Elapsed.ToString(@"hh\:mm\:ss");

            _ = _discordRpc.UpdatePresenceAsync(
                vnTitle,
                CurrentVN != null ? "Paused (Disconnected)" : "No Session",
                CurrentVN != null ? $"{Tracker.ValidCharacterCount} chars | {elapsedStr}" : "Waiting...",
                null,
                imageUrl
            );
        }
    }
}