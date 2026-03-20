using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using VN2Anki.Messages;
using VN2Anki.Models.Entities;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.Services
{
    public class VnLinkerService : IVnLinkerService
    {
        private readonly ISessionManagerService _sessionManager;
        private readonly IConfigurationService _configService;
        private readonly VideoEngine _videoEngine;
        private readonly MiningService _miningService;
        private readonly IVnDatabaseService _vnDatabaseService;
        private readonly IDispatcherService _dispatcherService;
        private readonly IWindowService _windowService;
        private readonly IExternalToolService _externalToolService;

        public VnLinkerService(
            ISessionManagerService sessionManager,
            IConfigurationService configService,
            VideoEngine videoEngine,
            MiningService miningService,
            IVnDatabaseService vnDatabaseService,
            IDispatcherService dispatcherService,
            IWindowService windowService,
            IExternalToolService externalToolService)
        {
            _sessionManager = sessionManager;
            _configService = configService;
            _videoEngine = videoEngine;
            _miningService = miningService;
            _vnDatabaseService = vnDatabaseService;
            _dispatcherService = dispatcherService;
            _windowService = windowService;
            _externalToolService = externalToolService;
        }

        private async Task<VisualNovel?> AutoSyncRunningVnAsync(string? specificProcessName = null)
        {
            if (_sessionManager.HasUnsavedProgress)
            {
                return null;
            }

            var vnsDb = await _vnDatabaseService.GetAllVisualNovelsAsync();

            if (vnsDb.Count == 0) return null;

            var runningWindows = _videoEngine.GetWindows();
            var matchedVns = new List<VisualNovel>();

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

            if (matchedVns.Count == 0)
            {
                 return null;
            }

            VisualNovel? selectedVn = null;
            var silentSync = _configService.CurrentConfig.Session.SilentSync;

            // the UI prompts must happen in the UI thread. IDispatcherService allows safe invocation.
            _dispatcherService.Invoke(() =>
            {
                if (matchedVns.Count == 1)
                {
                    if (!silentSync)
                    {
                        bool result = _windowService.ShowConfirmation(
                            string.Format(Locales.Strings.MsgConfirmVnDetected, matchedVns[0].Title),
                            Locales.Strings.MsgAttention);

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
                // Removed implicit _configService.Save() here to avoid unwanted config pollution during AutoSync

                _miningService.TargetVideoWindow = config.Media.VideoWindow;

                if (string.IsNullOrEmpty(specificProcessName) && targetWin != null)
                {
                    WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = string.Format(Locales.Strings.MsgSessionLinked, selectedVn.Title), IsError = false }));
                }

                if (targetWin != null && targetWin.ProcessId > 0)
                {
                     _ = _externalToolService.LaunchHookerAsync(selectedVn, targetWin.ProcessId);
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
                    _miningService.TargetVideoWindow = string.Empty;
                }
                return null;
            }
        }

        public async Task<VisualNovel?> TryAutoLinkAsync(VisualNovel? currentVn, string? specificProcessName = null)
        {
            var selectedVn = await AutoSyncRunningVnAsync(specificProcessName);
            if (selectedVn != null)
            {
                return selectedVn;
            }

            var config = _configService.CurrentConfig;
            var configWin = config.Media.VideoWindow;

            if (string.IsNullOrEmpty(configWin))
            {
                return null;
            }

            // Check if the process in config actually exists
            var windows = _videoEngine.GetWindows();
            bool exists = windows.Any(w => string.Equals(w.ProcessName, configWin, StringComparison.OrdinalIgnoreCase));

            if (!exists)
            {
                // Ghost detected! Clear the config so it doesn't haunt the UI or services
                config.Media.VideoWindow = string.Empty;
                // Implicit save removed here as well to respect UI state.
                _miningService.TargetVideoWindow = string.Empty;
                return null;
            }

            if (currentVn != null)
            {
                if (!string.Equals(currentVn.ProcessName, configWin, StringComparison.OrdinalIgnoreCase))
                {
                    string exeName = !string.IsNullOrEmpty(currentVn.ExecutablePath) ? Path.GetFileName(currentVn.ExecutablePath) : "";
                    if (string.IsNullOrEmpty(exeName) || !string.Equals(exeName, configWin, StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }
                }
            }

            return currentVn;
        }
    }
}