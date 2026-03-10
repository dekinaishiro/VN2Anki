using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VN2Anki.Models.Entities;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.Services
{
    public class GameLauncherService : IGameLauncherService
    {
        private readonly VideoEngine _videoEngine;
        private readonly IConfigurationService _configService;
        private readonly MiningService _miningService;
        private readonly IExternalToolService _externalToolService;

        public GameLauncherService(VideoEngine videoEngine, IConfigurationService configService, MiningService miningService, IExternalToolService externalToolService)
        {
            _videoEngine = videoEngine;
            _configService = configService;
            _miningService = miningService;
            _externalToolService = externalToolService;
        }

        public async Task<GameLaunchResult> LaunchAndHookAsync(VisualNovel vn, CancellationToken token)
        {
            if (string.IsNullOrEmpty(vn.ExecutablePath) || !File.Exists(vn.ExecutablePath))
            {
                return GameLaunchResult.ExecutableNotFound;
            }

            try
            {
                var pInfo = new ProcessStartInfo
                {
                    FileName = vn.ExecutablePath,
                    WorkingDirectory = Path.GetDirectoryName(vn.ExecutablePath),
                    UseShellExecute = true
                };
                Process.Start(pInfo);
            }
            catch (Exception)
            {
                return GameLaunchResult.LaunchFailed;
            }

            try
            {
                // token injected delay loop to wait for the game window to appear
                // timeout is hardcoded for now (30 iterations * 1000ms = 30 seconds)
                for (int i = 0; i < 30; i++)
                {
                    await Task.Delay(1000, token);

                    var windows = _videoEngine.GetWindows();
                    var targetWin = windows.FirstOrDefault(w => w.ExecutablePath == vn.ExecutablePath || w.ProcessName == vn.ProcessName);

                    if (targetWin != null)
                    {
                        var config = _configService.CurrentConfig;
                        config.Media.VideoWindow = targetWin.ProcessName;
                        _configService.Save();

                        _miningService.TargetVideoWindow = targetWin.ProcessName;
                        
                        if (targetWin.ProcessId > 0)
                        {
                            _ = _externalToolService.LaunchHookerAsync(vn, targetWin.ProcessId);
                        }

                        return GameLaunchResult.Success;
                    }
                }

                return GameLaunchResult.Timeout;
            }
            catch (TaskCanceledException)
            {
                // prevents overlapping polls or cancelled via cancellation token
                return GameLaunchResult.Cancelled;
            }
        }
    }
}