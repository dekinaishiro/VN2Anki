using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VN2Anki.Models.Entities;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.Services
{
    public class VnLinkerService : IVnLinkerService
    {
        private readonly IConfigurationService _configService;
        private readonly IProcessMonitoringService _processMonitor;
        private readonly IVnDatabaseService _vnDatabaseService;
        private readonly IDispatcherService _dispatcherService;
        private readonly IWindowService _windowService;

        public VnLinkerService(
            IConfigurationService configService,
            IProcessMonitoringService processMonitor,
            IVnDatabaseService vnDatabaseService,
            IDispatcherService dispatcherService,
            IWindowService windowService)
        {
            _configService = configService;
            _vnDatabaseService = vnDatabaseService;
            _dispatcherService = dispatcherService;
            _windowService = windowService;
            _processMonitor = processMonitor;
        }

        private async Task<LinkResult?> AutoSyncRunningVnAsync(string? specificProcessName = null, bool suppressConfirmation = false, int maxRetries = 0)
        {
            var matchedVns = new List<VisualNovel>();
            var vnsDb = await _vnDatabaseService.GetAllVisualNovelsAsync();
            if (vnsDb.Count == 0) return null;

            List<ActiveWindowItem> runningWindows = new();

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                runningWindows = _processMonitor.GetActiveWindows();
                var windowsToCheck = string.IsNullOrEmpty(specificProcessName)
                    ? runningWindows
                    : runningWindows.Where(w => w.ProcessName == specificProcessName).ToList();

                foreach (var win in windowsToCheck)
                {
                    var match = vnsDb.FirstOrDefault(v =>
                        (!string.IsNullOrEmpty(v.ExecutablePath) && !string.IsNullOrEmpty(win.ExecutablePath) && string.Equals(v.ExecutablePath, win.ExecutablePath, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(v.ProcessName) && string.Equals(v.ProcessName, win.ProcessName, StringComparison.OrdinalIgnoreCase)));

                    if (match != null && !matchedVns.Any(v => v.Id == match.Id))
                    {
                        matchedVns.Add(match);
                    }
                }

                if (matchedVns.Count > 0)
                {
                    break;
                }

                if (attempt < maxRetries)
                {
                    await Task.Delay(1000);
                }
            }

            if (matchedVns.Count == 0)
            {
                 return null;
            }

            VisualNovel? selectedVn = null;
            var silentSync = _configService.CurrentConfig.Session.SilentSync;

            if (suppressConfirmation || silentSync || matchedVns.Count == 1)
            {
                // If suppressed, or silent sync on, or just one match, we might still want to ask if silentSync is false and not suppressed.
                // Actually, let's keep the logic close to original but respect suppressConfirmation.
            }

            _dispatcherService.Invoke(() =>
            {
                if (matchedVns.Count == 1)
                {
                    if (!silentSync && !suppressConfirmation)
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
                if (targetWin != null)
                {
                    return new LinkResult
                    {
                        VisualNovel = selectedVn,
                        ProcessId = targetWin.ProcessId,
                        ProcessName = targetWin.ProcessName
                    };
                }
                
                return new LinkResult
                {
                    VisualNovel = selectedVn,
                    ProcessId = 0,
                    ProcessName = selectedVn.ProcessName
                };
            }

            return null;
        }

        public async Task<LinkResult?> TryAutoLinkAsync(VisualNovel? currentVn, string? specificProcessName = null, bool suppressConfirmation = false, int maxRetries = 0)
        {
            var result = await AutoSyncRunningVnAsync(specificProcessName, suppressConfirmation, maxRetries);
            if (result != null && result.ProcessId > 0)
            {
                return result;
            }

            // Also check by config directly
            var config = _configService.CurrentConfig;
            var configWin = config.Media.VideoWindow;

            if (!string.IsNullOrEmpty(configWin))
            {
                var windows = _processMonitor.GetActiveWindows();
                var targetWin = windows.FirstOrDefault(w => string.Equals(w.ProcessName, configWin, StringComparison.OrdinalIgnoreCase));

                if (targetWin != null && currentVn != null)
                {
                    bool isMatch = string.Equals(currentVn.ProcessName, configWin, StringComparison.OrdinalIgnoreCase);
                    if (!isMatch)
                    {
                        string exeName = !string.IsNullOrEmpty(currentVn.ExecutablePath) ? Path.GetFileName(currentVn.ExecutablePath) : "";
                        isMatch = !string.IsNullOrEmpty(exeName) && string.Equals(exeName, configWin, StringComparison.OrdinalIgnoreCase);
                    }

                    if (isMatch)
                    {
                        return new LinkResult
                        {
                            VisualNovel = currentVn,
                            ProcessId = targetWin.ProcessId,
                            ProcessName = targetWin.ProcessName
                        };
                    }
                }
            }

            return null;
        }
    }
}
