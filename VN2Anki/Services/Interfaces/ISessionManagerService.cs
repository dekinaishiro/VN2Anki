using System.Threading.Tasks;
using VN2Anki.Models.Entities;

namespace VN2Anki.Services.Interfaces
{
    public interface ISessionManagerService
    {
        VisualNovel? CurrentVN { get; }
        bool IsBufferActive { get; set; }
        bool HasUnsavedProgress { get; }
        bool ToggleBuffer(VisualNovel? currentVN);
        Task EndSessionAsync(VisualNovel? currentVN);
    }
}