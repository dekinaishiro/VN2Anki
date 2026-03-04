using System.Threading.Tasks;
using VN2Anki.Models.Entities;

namespace VN2Anki.Services.Interfaces
{
    public interface ISessionManagerService
    {
        bool IsBufferActive { get; set; }
        bool ToggleBuffer(VisualNovel currentVN);
        void EndSession(VisualNovel currentVN);
    }
}