using System.Threading.Tasks;
using VN2Anki.Models.Entities;

namespace VN2Anki.Services.Interfaces
{
    public enum GameLaunchResult
    {
        Success,
        ExecutableNotFound,
        LaunchFailed
    }

    public interface IGameLauncherService
    {
        Task<GameLaunchResult> LaunchGameAsync(VisualNovel vn);
    }
}
