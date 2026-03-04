using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Messaging;
using VN2Anki.Data;
using VN2Anki.Messages;
using VN2Anki.Models.Entities;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.Services
{
    public class SessionManagerService : ISessionManagerService
    {
        private readonly MiningService _miningService;
        private readonly IConfigurationService _configService;
        private readonly DiscordRpcService _discordRpc;
        private readonly IWindowService _windowService;
        private readonly IServiceProvider _serviceProvider;
        private readonly SessionTracker _tracker;
        private readonly IVnDatabaseService _vnDatabaseService;
        private readonly VideoEngine _videoEngine;
        private readonly IDispatcherService _dispatcherService;

        public bool IsBufferActive { get; set; }

        public SessionManagerService(
            SessionTracker tracker,
            MiningService miningService,
            IConfigurationService configService,
            DiscordRpcService discordRpc,
            IWindowService windowService,
            IServiceProvider serviceProvider,
            IVnDatabaseService vnDatabaseService,
            VideoEngine videoEngine,
            IDispatcherService dispatcherService)
        {
            _tracker = tracker;
            _miningService = miningService;
            _configService = configService;
            _discordRpc = discordRpc;
            _windowService = windowService;
            _serviceProvider = serviceProvider;
            _vnDatabaseService = vnDatabaseService;
            _videoEngine = videoEngine;
            _dispatcherService = dispatcherService;
        }

        public bool ToggleBuffer(VisualNovel currentVN)
        {
            if (!IsBufferActive)
            {
                var config = _configService.CurrentConfig;

                if (string.IsNullOrEmpty(config.Media.VideoWindow))
                {
                    WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Video Source vazia! Selecione o vídeo.", IsError = true }));
                    return false;
                }

                var procs = System.Diagnostics.Process.GetProcessesByName(config.Media.VideoWindow);
                bool isRunning = procs.Any(p => p.MainWindowHandle != IntPtr.Zero);
                foreach (var p in procs) p.Dispose();

                if (!isRunning)
                {
                    WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "A janela alvo está fechada! Abra o jogo e tente novamente.", IsError = true }));
                    return false;
                }

                var devices = _miningService.Audio.GetDevices();
                var deviceId = devices.FirstOrDefault(d => d.Name == config.Media.AudioDevice)?.Id;

                if (string.IsNullOrEmpty(deviceId))
                {
                    WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Configure o Áudio primeiro!", IsError = true }));
                    return false;
                }

                if (currentVN == null && _tracker.ValidCharacterCount == 0 && _tracker.Elapsed.TotalSeconds == 0)
                {
                    bool result = _windowService.ShowConfirmation(
                        "Nenhuma Visual Novel está vinculada a esta sessão.\nDeseja iniciar o rastreamento avulso mesmo assim?",
                        "Aviso de Sessão Vazia",
                        true);

                    if (!result) return false;
                }

                _miningService.StartBuffer(deviceId);
                IsBufferActive = true;

                string vnTitle = currentVN?.Title ?? "Reading a VN";
                string imageUrl = currentVN?.CoverImageUrl ?? "default_icon";
                DateTime startTime = DateTime.UtcNow.Subtract(_tracker.Elapsed);
                _ = _discordRpc.UpdatePresenceAsync(
                    vnTitle,
                    "Reading",
                    $"{_tracker.ValidCharacterCount} chars",
                    startTime,
                    imageUrl
                );
            }
            else
            {
                _miningService.StopBuffer();
                IsBufferActive = false;

                string vnTitle = currentVN?.Title ?? "Reading a VN";
                string imageUrl = currentVN?.CoverImageUrl ?? "default_icon";
                string elapsedStr = _tracker.Elapsed.ToString(@"hh\:mm\:ss");
                _ = _discordRpc.UpdatePresenceAsync(
                    vnTitle,
                    "Paused",
                    $"{_tracker.ValidCharacterCount} chars | {elapsedStr}",
                    null,
                    imageUrl
                );
            }

            return IsBufferActive;
        }

        public void EndSession(VisualNovel currentVN)
        {
            bool hasProgress = _tracker.Elapsed.TotalSeconds > 0 || _tracker.ValidCharacterCount > 0;

            if (hasProgress)
            {
                int? vnIdToSave = currentVN?.Id;

                if (currentVN == null)
                {
                    bool result = _windowService.ShowConfirmation(
                        "Você leu alguns caracteres, mas nenhuma Visual Novel está selecionada.\nDeseja salvar este progresso no histórico para vinculá-lo a um jogo mais tarde?",
                        "Salvar Sessão Órfã?");

                    if (!result)
                    {
                        hasProgress = false;
                    }
                }

                if (hasProgress)
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var record = new SessionRecord
                        {
                            VisualNovelId = vnIdToSave,
                            StartTime = System.DateTime.Now - _tracker.Elapsed,
                            EndTime = System.DateTime.Now,
                            DurationSeconds = (int)_tracker.Elapsed.TotalSeconds,
                            CharactersRead = _tracker.ValidCharacterCount,
                            CardsMined = 0
                        };
                        db.Sessions.Add(record);
                        db.SaveChanges();
                    }

                    WeakReferenceMessenger.Default.Send(new SessionSavedMessage());
                }
            }

            _ = _discordRpc.ClearPresenceAsync();
            if (IsBufferActive)
            {
                _miningService.StopBuffer();
                IsBufferActive = false;
            }
            _tracker.Reset();
            foreach (var slot in _miningService.HistorySlots) slot.Dispose();
            _miningService.HistorySlots.Clear();
            
            WeakReferenceMessenger.Default.Send(new SessionEndedMessage());
        }

        public async Task<VisualNovel> AutoSyncRunningVnAsync(string specificProcessName = null)
        {
            if (IsBufferActive || _tracker.ValidCharacterCount > 0 || _tracker.Elapsed.TotalSeconds > 0)
            {
                return null;
            }

            await Task.Delay(1000);

            var vnsDb = await _vnDatabaseService.GetAllVisualNovelsAsync();

            if (vnsDb.Count == 0) return null;

            var runningWindows = _videoEngine.GetWindows();
            var matchedVns = new List<VisualNovel>();

            var windowsToCheck = string.IsNullOrEmpty(specificProcessName)
                ? runningWindows
                : runningWindows.Where(w => w.ProcessName == specificProcessName).ToList();

            // When specificProcessName is provided (e.g. settings changed), and it's NOT currently running,
            // we should still try to find a DB match to set it as CurrentVN visually, even if we can't hook the window yet.
            if (!string.IsNullOrEmpty(specificProcessName) && windowsToCheck.Count == 0)
            {
                var fallbackMatch = vnsDb.FirstOrDefault(v => v.ProcessName == specificProcessName);
                if (fallbackMatch != null)
                {
                    return fallbackMatch; 
                }
                return null;
            }

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

            if (matchedVns.Count == 0)
            {
                 // If no running window matches, check if the specific process is at least a known VN
                 if (!string.IsNullOrEmpty(specificProcessName))
                 {
                     return vnsDb.FirstOrDefault(v => v.ProcessName == specificProcessName);
                 }
                 return null;
            }

            VisualNovel selectedVn = null;

            // the UI prompts must happen in the UI thread. IDispatcherService allows safe invocation.
            _dispatcherService.Invoke(() =>
            {
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
            });

            if (selectedVn != null)
            {
                var targetWin = runningWindows.FirstOrDefault(w => w.ExecutablePath == selectedVn.ExecutablePath || w.ProcessName == selectedVn.ProcessName);

                var config = _configService.CurrentConfig;
                config.Media.VideoWindow = targetWin?.ProcessName ?? selectedVn.ProcessName;
                _configService.Save();

                _miningService.TargetVideoWindow = config.Media.VideoWindow;

                if (string.IsNullOrEmpty(specificProcessName) && targetWin != null)
                {
                    WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = $"Sessão vinculada: {selectedVn.Title}", IsError = false }));
                }

                return selectedVn;
            }
            else
            {
                var config = _configService.CurrentConfig;
                var savedProcess = config.Media.VideoWindow;

                if (!string.IsNullOrEmpty(savedProcess) && matchedVns.Any(v => v.ProcessName == savedProcess))
                {
                    config.Media.VideoWindow = string.Empty;
                    _configService.Save();
                    _miningService.TargetVideoWindow = string.Empty;
                }
                return null;
            }
        }
        public bool PerformIdleCheck()
        {
            bool isZeroed = _tracker.ValidCharacterCount == 0 && _tracker.Elapsed.TotalSeconds == 0 && !IsBufferActive;
            if (!isZeroed) return false;

            var videoSource = _configService.CurrentConfig.Media.VideoWindow;
            if (string.IsNullOrEmpty(videoSource)) return false; 

            var procs = System.Diagnostics.Process.GetProcessesByName(videoSource);
            bool isRunning = false;
            foreach (var p in procs)
            {
                if (p.MainWindowHandle != IntPtr.Zero) isRunning = true;
                p.Dispose();
            }

            if (!isRunning)
            {
                var config = _configService.CurrentConfig;
                config.Media.VideoWindow = string.Empty;
                _configService.Save();
                _miningService.TargetVideoWindow = string.Empty;
                return true; 
            }

            return false;
        }
    }
}