using System.Collections.Generic;
using System.Threading.Tasks;
using VN2Anki.Models.Entities;

namespace VN2Anki.Services.Interfaces
{
    public interface IVnDatabaseService
    {
        Task<List<VisualNovel>> GetAllVisualNovelsAsync();
    }
}