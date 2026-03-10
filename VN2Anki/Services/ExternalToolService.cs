using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VN2Anki.Models.Entities;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.Services
{
    public class ExternalToolService : IExternalToolService
    {
        private readonly IConfigurationService _configService;
        private readonly ILogger<ExternalToolService> _logger;

        public ExternalToolService(IConfigurationService configService, ILogger<ExternalToolService> logger)
        {
            _configService = configService;
            _logger = logger;
        }

        public Task LaunchHookerAsync(VisualNovel vn, int processId)
        {
            return Task.Run(() =>
            {
                if (vn == null)
                {
                    _logger.LogWarning("LaunchHookerAsync aborted: VisualNovel is null.");
                    return;
                }
                
                if (processId <= 0)
                {
                    _logger.LogWarning("LaunchHookerAsync aborted: Invalid ProcessId ({ProcessId}) for VN {Title}.", processId, vn.Title);
                    return;
                }

                var config = _configService.CurrentConfig.Hook;
                _logger.LogInformation("Attempting to launch hooker for process {ProcessId}. AutoLaunch config is set to: {AutoLaunchHooker}", processId, config.AutoLaunchHooker);

                if (string.Equals(config.AutoLaunchHooker, "Textractor", StringComparison.OrdinalIgnoreCase))
                {
                    LaunchTextractor(vn, processId, config.TextractorBasePath);
                }
                else if (string.Equals(config.AutoLaunchHooker, "LunaTranslator", StringComparison.OrdinalIgnoreCase))
                {
                    LaunchLunaTranslator(processId, config.LunaTranslatorPath);
                }
                else
                {
                    _logger.LogInformation("No valid AutoLaunchHooker selected (Current: {AutoLaunchHooker}). Skipping hooker launch.", config.AutoLaunchHooker);
                }
            });
        }

        private void LaunchTextractor(VisualNovel vn, int processId, string basePath)
        {
            if (string.IsNullOrWhiteSpace(basePath))
            {
                _logger.LogWarning("LaunchTextractor failed: TextractorBasePath is empty.");
                return;
            }

            string archFolder = string.Equals(vn.Architecture, "x64", StringComparison.OrdinalIgnoreCase) ? "x64" : "x86";
            string exePath = Path.Combine(basePath, archFolder, "Textractor.exe");

            _logger.LogInformation("Checking Textractor executable at: {ExePath}", exePath);

            if (!File.Exists(exePath))
            {
                _logger.LogWarning("Textractor not found in arch folder. Fallback to base path: {BasePath}", basePath);
                // Fallback to direct path if the user didn't select a base folder but selected the exact exe folder
                exePath = Path.Combine(basePath, "Textractor.exe");
                if (!File.Exists(exePath))
                {
                    _logger.LogError("LaunchTextractor completely failed: Executable not found at {ExePath}", exePath);
                    return;
                }
            }

            try
            {
                _logger.LogInformation("Starting Textractor with arguments: -P{ProcessId}", processId);
                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"-P{processId}",
                    UseShellExecute = true
                };
                Process.Start(startInfo);
                _logger.LogInformation("Textractor launched successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to launch Textractor.");
            }
        }

        private void LaunchLunaTranslator(int processId, string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath))
            {
                _logger.LogWarning("LaunchLunaTranslator failed: LunaTranslatorPath is empty.");
                return;
            }

            _logger.LogInformation("Checking LunaTranslator executable at: {ExePath}", exePath);

            if (!File.Exists(exePath))
            {
                _logger.LogError("LaunchLunaTranslator failed: Executable not found at {ExePath}", exePath);
                return;
            }

            try
            {
                _logger.LogInformation("Starting LunaTranslator without arguments (Auto-attach relies on Luna's internal settings).");
                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath)
                };
                Process.Start(startInfo);
                _logger.LogInformation("LunaTranslator launched successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to launch LunaTranslator.");
            }
        }
    }
}