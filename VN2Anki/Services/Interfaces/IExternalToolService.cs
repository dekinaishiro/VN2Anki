using System.Threading.Tasks;
using VN2Anki.Models.Entities;

namespace VN2Anki.Services.Interfaces
{
    public interface IExternalToolService
    {
        Task LaunchHookerAsync(VisualNovel vn, int processId);
    }
}