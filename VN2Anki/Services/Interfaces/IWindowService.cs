using System.Collections.Generic;
using VN2Anki.Models.Entities;

namespace VN2Anki.Services.Interfaces
{
    public interface IWindowService
    {
        void CloseWindow(object viewModel, bool dialogResult);
        VisualNovel ShowMultipleVnPrompt(List<VisualNovel> vns);
    }
}