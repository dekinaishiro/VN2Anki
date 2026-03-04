using System.Threading;
using System.Threading.Tasks;
using VN2Anki.Models.Entities;

namespace VN2Anki.Services.Interfaces
{
    public enum GameLaunchResult
    {
        Success,
        ExecutableNotFound,
        LaunchFailed,
        Timeout,
        Cancelled
    }

    public interface IGameLauncherService
    {
        Task<GameLaunchResult> LaunchAndHookAsync(VisualNovel vn, CancellationToken token);
    }
}