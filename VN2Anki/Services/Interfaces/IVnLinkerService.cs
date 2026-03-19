using System.Threading.Tasks;
using VN2Anki.Models.Entities;

namespace VN2Anki.Services.Interfaces
{
    public interface IVnLinkerService
    {
        Task<VisualNovel?> TryAutoLinkAsync(VisualNovel? currentVn, string? specificProcessName = null);
    }
}
