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
        private readonly IConfigurationService _configService;
        private readonly VideoEngine _videoEngine;
        private readonly IVnDatabaseService _vnDatabaseService;
        private readonly IDispatcherService _dispatcherService;
        private readonly IWindowService _windowService;
        private readonly IExternalToolService _externalToolService;

        public VnLinkerService(
            IConfigurationService configService,
            VideoEngine videoEngine,
            IVnDatabaseService vnDatabaseService,
            IDispatcherService dispatcherService,
            IWindowService windowService,
            IExternalToolService externalToolService)
        {
            _configService = configService;
            _vnDatabaseService = vnDatabaseService;
            _dispatcherService = dispatcherService;
            _windowService = windowService;
            _externalToolService = externalToolService;

            // duvidoso
            _videoEngine = videoEngine;
        }

        private async Task<VisualNovel?> AutoSyncRunningVnAsync(string? specificProcessName = null)
        {
            // declares list to hold matched VNs based on running processes
            var matchedVns = new List<VisualNovel>();
            
            // gets list of all VNs and their associated process info from the database
            var vnsDb = await _vnDatabaseService.GetAllVisualNovelsAsync();
            if (vnsDb.Count == 0) return null;

            // gets list of currently running windows with process info from the video engine
            var runningWindows = _videoEngine.GetWindows();

            // filters running windows based on specific process name if provided
            var windowsToCheck = string.IsNullOrEmpty(specificProcessName)
                ? runningWindows
                : runningWindows.Where(w => w.ProcessName == specificProcessName).ToList();

            // checks for each running window if it matches any VN in the
            // database based on executable path or process name
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

            // probably should not be here
            // basically prompts the user if they want add the detected vn to the current session
            // maybe a centralized prompt service would be better for this kind of thing in the since it
            // organizes all messages in one place, can be reused across the app, makes it easier to manage different types of prompts
            // and makes the rsx variables easier to manage, etc
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
                // finds the target window that matches the selected VN based on executable path or process name
                var targetWin = runningWindows.FirstOrDefault(w => w.ExecutablePath == selectedVn.ExecutablePath || w.ProcessName == selectedVn.ProcessName);

                // updates video window config
                var config = _configService.CurrentConfig;
                config.Media.VideoWindow = targetWin?.ProcessName ?? selectedVn.ProcessName;

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
                // empty the config if the specified process doesn't exist to avoid repeated failed attempts in the future
                config.Media.VideoWindow = string.Empty;
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