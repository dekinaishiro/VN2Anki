using System.Collections.Generic;
using System.Threading.Tasks;
using VN2Anki.Models.Entities;

namespace VN2Anki.Services.Interfaces
{
    public interface IVnDatabaseService
    {
        Task<List<VisualNovel>> GetAllVisualNovelsAsync();
        Task AddVisualNovelAsync(VisualNovel vn);
        Task UpdateVisualNovelAsync(VisualNovel vn);
        Task DeleteVisualNovelAsync(VisualNovel vn);
        Task<bool> ExistsByVndbIdAsync(string vndbId);
        
        Task<List<SessionRecord>> GetAllSessionsAsync();
        Task DeleteSessionAsync(SessionRecord session);
        Task AddSessionAsync(SessionRecord session);
    }
}