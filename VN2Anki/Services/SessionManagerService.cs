using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using VN2Anki.Messages;
using VN2Anki.Models.Entities;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.Services
{
    public class SessionManagerService : ISessionManagerService
    {
        private readonly MiningService _miningService;
        private readonly IConfigurationService _configService;
        private readonly IWindowService _windowService;
        private readonly SessionTracker _tracker;
        private readonly IVnDatabaseService _vnDatabaseService;
        private readonly ISessionLoggerService _sessionLogger;

        public bool IsBufferActive { get; set; }

        public bool HasUnsavedProgress => IsBufferActive || (_tracker != null && (_tracker.Elapsed.TotalSeconds > 0 || _tracker.ValidCharacterCount > 0));

        public SessionManagerService(
            SessionTracker tracker,
            MiningService miningService,
            IConfigurationService configService,
            IWindowService windowService,
            IVnDatabaseService vnDatabaseService,
            ISessionLoggerService sessionLogger)
        {
            _tracker = tracker;
            _miningService = miningService;
            _sessionLogger = sessionLogger;
            _windowService = windowService;
            _vnDatabaseService = vnDatabaseService;

            _configService = configService;
        }

        public bool ToggleBuffer(VisualNovel? currentVN)
        {
            if (!IsBufferActive)
            {

                // 
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

                if (currentVN == null && _tracker.ValidCharacterCount == 0 && _tracker.Elapsed.TotalSeconds == 0)
                {
                    bool result = _windowService.ShowConfirmation(
                        Locales.Strings.MsgConfirmStartTrackingWithoutVn,
                        Locales.Strings.TitleEmptySessionWarning,
                        true);

                    if (!result) return false;
                }

                _ = _sessionLogger.StartNewSessionAsync();
                _miningService.StartBuffer(deviceId);
                IsBufferActive = true;

                WeakReferenceMessenger.Default.Send(new BufferStartedMessage());
                WeakReferenceMessenger.Default.Send(new SessionStartedMessage(currentVN));
            }
            else
            {
                _miningService.StopBuffer();
                IsBufferActive = false;

                WeakReferenceMessenger.Default.Send(new BufferStoppedMessage());
            }

            return IsBufferActive;
        }

        public async Task EndSessionAsync(VisualNovel? currentVN)
        {
            bool hasProgress = _tracker.Elapsed.TotalSeconds > 0 || _tracker.ValidCharacterCount > 0;
            bool saved = false;
            SessionRecord? savedRecord = null;

            if (hasProgress)
            {
                int? vnIdToSave = currentVN?.Id;

                if (currentVN == null)
                {
                    bool result = _windowService.ShowConfirmation(
                        Locales.Strings.MsgConfirmSaveOrphanSession,
                        Locales.Strings.TitleSaveOrphanSession);

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
                        // Fix 3.B: Saving UTC internally if possible, but keeping local logic if UI relies on it. 
                        // It's safer to use local time for now since the database mapping might not support offsets.
                        StartTime = DateTime.Now - _tracker.Elapsed,
                        EndTime = DateTime.Now,
                        DurationSeconds = (int)_tracker.Elapsed.TotalSeconds,
                        CharactersRead = _tracker.ValidCharacterCount,
                        CardsMined = 0, // This is probably populated elsewhere or by mining actions
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