using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

        public VnLinkerService(
            ISessionManagerService sessionManager,
            IConfigurationService configService,
            VideoEngine videoEngine,
            MiningService miningService)
        {
            _sessionManager = sessionManager;
            _configService = configService;
            _videoEngine = videoEngine;
            _miningService = miningService;
        }

        public async Task<VisualNovel?> TryAutoLinkAsync(VisualNovel? currentVn, string? specificProcessName = null)
        {
            var selectedVn = await _sessionManager.AutoSyncRunningVnAsync(specificProcessName);
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
                _configService.Save();
                _miningService.TargetVideoWindow = string.Empty;
                return null;
            }

            if (currentVn != null)
            {
                // If we have a CurrentVN but the current config video window doesn't match its ProcessName or ExecutablePath,
                // it means the user manually selected a different, unlinked window. We must unlink the session visually.
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
