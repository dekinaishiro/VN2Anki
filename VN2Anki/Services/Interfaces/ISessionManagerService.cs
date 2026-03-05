using System.Threading.Tasks;
using VN2Anki.Models.Entities;

namespace VN2Anki.Services.Interfaces
{
    public interface ISessionManagerService
    {
        bool IsBufferActive { get; set; }
        bool HasUnsavedProgress { get; }
        bool ToggleBuffer(VisualNovel currentVN);
        void EndSession(VisualNovel currentVN);
        Task<VisualNovel> AutoSyncRunningVnAsync(string specificProcessName = null);
        bool PerformIdleCheck();
    }
}