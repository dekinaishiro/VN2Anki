using System.Threading.Tasks;
using VN2Anki.Models.Entities;

namespace VN2Anki.Services.Interfaces
{
    public class LinkResult
    {
        public VisualNovel VisualNovel { get; set; }
        public int ProcessId { get; set; }
        public string ProcessName { get; set; }
    }

    public interface IVnLinkerService
    {
        Task<LinkResult?> TryAutoLinkAsync(VisualNovel? currentVn, string? specificProcessName = null, bool suppressConfirmation = false, int maxRetries = 0);
    }
}
