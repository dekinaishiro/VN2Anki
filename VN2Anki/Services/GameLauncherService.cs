using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using VN2Anki.Models.Entities;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.Services
{
    public class GameLauncherService : IGameLauncherService
    {
        public Task<GameLaunchResult> LaunchGameAsync(VisualNovel vn)
        {
            if (string.IsNullOrEmpty(vn.ExecutablePath) || !File.Exists(vn.ExecutablePath))
            {
                return Task.FromResult(GameLaunchResult.ExecutableNotFound);
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
                return Task.FromResult(GameLaunchResult.Success);
            }
            catch (Exception)
            {
                return Task.FromResult(GameLaunchResult.LaunchFailed);
            }
        }
    }
}
