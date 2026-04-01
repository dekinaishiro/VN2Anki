using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using VN2Anki.Messages;
using VN2Anki.Models.Entities;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.Services
{
    public class SessionManagerService : ISessionManagerService, IRecipient<PlayVnMessage>
    {
        private readonly MiningService _miningService;
        private readonly IConfigurationService _configService;
        private readonly IWindowService _windowService;
        private readonly SessionTracker _tracker;
        private readonly IVnDatabaseService _vnDatabaseService;
        private readonly ISessionLoggerService _sessionLogger;
        private readonly IProcessMonitoringService _processMonitor;
        private readonly IVnLinkerService _linkerService;
        private readonly IGameLauncherService _gameLauncher;
        private readonly IDispatcherService _dispatcherService;
        private readonly IExternalToolService _externalToolService;

        private VisualNovel? _currentVN;
        public VisualNovel? CurrentVN => _currentVN;
        
        private int? _pendingLaunchVnId = null;

        public bool IsBufferActive { get; set; }

        public bool HasUnsavedProgress => IsBufferActive || (_tracker != null && (_tracker.Elapsed.TotalSeconds > 0 || _tracker.ValidCharacterCount > 0));

        public SessionManagerService(
            SessionTracker tracker,
            MiningService miningService,
            IConfigurationService configService,
            IWindowService windowService,
            IVnDatabaseService vnDatabaseService,
            ISessionLoggerService sessionLogger,
            IProcessMonitoringService processMonitor,
            IVnLinkerService linkerService,
            IGameLauncherService gameLauncher,
            IDispatcherService dispatcherService,
            IExternalToolService externalToolService)
        {
            _tracker = tracker;
            _miningService = miningService;
            _sessionLogger = sessionLogger;
            _windowService = windowService;
            _vnDatabaseService = vnDatabaseService;
            _configService = configService;
            _processMonitor = processMonitor;
            _linkerService = linkerService;
            _gameLauncher = gameLauncher;
            _dispatcherService = dispatcherService;
            _externalToolService = externalToolService;

            _processMonitor.VnProcessStarted += OnVnProcessStarted;
            _processMonitor.VnProcessStopped += OnVnProcessStopped;

            WeakReferenceMessenger.Default.RegisterAll(this);
        }

        public async Task TryAutoLinkAsync(string? specificProcessName = null, bool suppressConfirmation = false, int maxRetries = 0, VisualNovel? expectedVn = null)
        {
            if (HasUnsavedProgress) return;

            var vnToPass = expectedVn ?? _currentVN;
            var linkResult = await _linkerService.TryAutoLinkAsync(vnToPass, specificProcessName, suppressConfirmation, maxRetries);
            
            if (linkResult != null)
            {
                var vn = linkResult.VisualNovel;
                var config = _configService.CurrentConfig;
                config.Media.VideoWindow = linkResult.ProcessName;
                _configService.Save();

                if (string.IsNullOrEmpty(specificProcessName))
                {
                    WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = string.Format(Locales.Strings.MsgSessionLinked, vn.Title), IsError = false }));
                }

                if (linkResult.ProcessId > 0)
                {
                    _ = _externalToolService.LaunchHookerAsync(vn, linkResult.ProcessId);
                }

                SetCurrentVN(vn, forceNotify: true);
            }
            else
            {
                SetCurrentVN(null, forceNotify: true);
            }
        }

        private async void OnVnProcessStarted(object? s, VnProcessEventArgs e)
        {
            bool isPending = _pendingLaunchVnId == e.VisualNovel.Id;
            if (isPending)
            {
                _pendingLaunchVnId = null;
            }

            if (_currentVN != null && _currentVN.Id == e.VisualNovel.Id)
            {
                return;
            }

            if (_currentVN != null && HasUnsavedProgress)
            {
                bool result = false;
                _dispatcherService.Invoke(() =>
                {
                    result = _windowService.ShowConfirmation(
                        string.Format(Locales.Strings.MsgConfirmChangeSession, _currentVN.Title, e.VisualNovel.Title), 
                        Locales.Strings.MsgAttention);
                });

                if (result)
                {
                    await EndSessionAsync(_currentVN);
                }
                else
                {
                    return;
                }
            }

            await TryAutoLinkAsync(e.VisualNovel.ProcessName, suppressConfirmation: isPending, maxRetries: 10, expectedVn: e.VisualNovel);
        }

        private async void OnVnProcessStopped(object? s, VnProcessEventArgs e)
        {
            if (_processMonitor.IsAnyInstanceRunning(e.VisualNovel.Id)) return;

            var config = _configService.CurrentConfig;
            if (_currentVN != null && _currentVN.Id == e.VisualNovel.Id)
            {
                bool isZeroed = _tracker.ValidCharacterCount == 0 && _tracker.Elapsed.TotalSeconds == 0 && !IsBufferActive;
                if (!isZeroed)
                {
                    // Força o encerramento da sessão e salva o progresso
                    await EndSessionAsync(_currentVN);
                    WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload 
                    { 
                        Message = string.Format("Sessão salva! O processo de {0} foi encerrado.", e.VisualNovel.Title), 
                        IsError = false 
                    }));
                }
                
                config.Media.VideoWindow = string.Empty;
                _configService.Save();
                SetCurrentVN(null);
            }
            else if (string.Equals(e.VisualNovel.ProcessName, config.Media.VideoWindow, StringComparison.OrdinalIgnoreCase))
            {
                config.Media.VideoWindow = string.Empty;
                _configService.Save();
                SetCurrentVN(null);
            }
        }

        private void SetCurrentVN(VisualNovel? vn, bool forceNotify = false)
        {
            if (!forceNotify && _currentVN?.Id == vn?.Id) return;

            _currentVN = vn;

            if (vn != null)
            {
                WeakReferenceMessenger.Default.Send(new CurrentVnChangedMessage(vn));
            }
            else
            {
                WeakReferenceMessenger.Default.Send(new CurrentVnUnlinkedMessage());
            }
        }

        public async void Receive(PlayVnMessage message)
        {
            try
            {
                var vn = message.VisualNovel;
                _pendingLaunchVnId = vn.Id;

                var launchResult = await _gameLauncher.LaunchGameAsync(vn);

                switch (launchResult)
                {
                    case GameLaunchResult.ExecutableNotFound:
                        WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = Locales.Strings.MsgExeNotFound, IsError = true }));
                        _pendingLaunchVnId = null;
                        break;
                    case GameLaunchResult.LaunchFailed:
                        WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Erro ao abrir o jogo!", IsError = true }));
                        _pendingLaunchVnId = null;
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in SessionManagerService.Receive(PlayVnMessage): {ex}");
                _pendingLaunchVnId = null;
            }
        }

        public bool ToggleBuffer(VisualNovel? currentVN = null)
        {
            var vn = currentVN ?? _currentVN;

            if (!IsBufferActive)
            {
                var config = _configService.CurrentConfig;

                if (string.IsNullOrEmpty(config.Media.VideoWindow))
                {
                    WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = Locales.Strings.MsgVideoSourceEmpty, IsError = true }));
                    return false;
                }

                var procs = System.Diagnostics.Process.GetProcessesByName(config.Media.VideoWindow);
                bool isRunning = procs.Any(p => p.MainWindowHandle != IntPtr.Zero);
                foreach (var p in procs) p.Dispose();

                if (!isRunning)
                {
                    WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = Locales.Strings.MsgTargetWindowClosed, IsError = true }));
                    return false;
                }

                var devices = _miningService.Audio.GetDevices();
                var deviceId = devices.FirstOrDefault(d => d.Name == config.Media.AudioDevice)?.Id;

                if (string.IsNullOrEmpty(deviceId))
                {
                    WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = Locales.Strings.MsgConfigureAudioFirst, IsError = true }));
                    return false;
                }

                if (vn == null && _tracker.ValidCharacterCount == 0 && _tracker.Elapsed.TotalSeconds == 0)
                {
                    bool result = false;
                    _dispatcherService.Invoke(() =>
                    {
                        result = _windowService.ShowConfirmation(
                            Locales.Strings.MsgConfirmStartTrackingWithoutVn,
                            Locales.Strings.TitleEmptySessionWarning,
                            true);
                    });

                    if (!result) return false;
                }

                _ = _sessionLogger.StartNewSessionAsync();
                _miningService.StartBuffer(deviceId);
                IsBufferActive = true;

                WeakReferenceMessenger.Default.Send(new BufferStartedMessage());
                WeakReferenceMessenger.Default.Send(new SessionStartedMessage(vn));
            }
            else
            {
                _miningService.StopBuffer();
                IsBufferActive = false;

                WeakReferenceMessenger.Default.Send(new BufferStoppedMessage());
            }

            return IsBufferActive;
        }

        public async Task EndSessionAsync(VisualNovel? currentVN = null)
        {
            var vn = currentVN ?? _currentVN;
            bool hasProgress = _tracker.Elapsed.TotalSeconds > 0 || _tracker.ValidCharacterCount > 0;
            bool saved = false;
            SessionRecord? savedRecord = null;

            if (hasProgress)
            {
                int? vnIdToSave = vn?.Id;

                if (vn == null)
                {
                    bool result = false;
                    _dispatcherService.Invoke(() =>
                    {
                        result = _windowService.ShowConfirmation(
                            Locales.Strings.MsgConfirmSaveOrphanSession,
                            Locales.Strings.TitleSaveOrphanSession);
                    });

                    if (!result)
                    {
                        hasProgress = false;
                    }
                }

                if (hasProgress)
                {
                    savedRecord = new SessionRecord
                    {
                        VisualNovelId = vnIdToSave,
                        StartTime = DateTime.Now - _tracker.Elapsed,
                        EndTime = DateTime.Now,
                        DurationSeconds = (int)_tracker.Elapsed.TotalSeconds,
                        CharactersRead = _tracker.ValidCharacterCount,
                        CardsMined = 0,
                        LogFilePath = _sessionLogger.CurrentLogPath
                    };
                    
                    await _vnDatabaseService.AddSessionAsync(savedRecord);

                    saved = true;
                    WeakReferenceMessenger.Default.Send(new SessionSavedMessage());
                }
            }

            await _sessionLogger.EndSessionAsync(discard: !saved);

            if (IsBufferActive)
            {
                _miningService.StopBuffer();
                IsBufferActive = false;
            }
            
            _miningService.ClearHistorySlots();
            
            WeakReferenceMessenger.Default.Send(new SessionEndedMessage(savedRecord));
        }
    }
}
