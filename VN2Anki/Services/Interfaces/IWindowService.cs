using System.Collections.Generic;
using VN2Anki.Models.Entities;

namespace VN2Anki.Services.Interfaces
{
    public interface IWindowService
    {
        void CloseWindow(object viewModel, bool dialogResult);
        VisualNovel ShowMultipleVnPrompt(List<VisualNovel> vns);
        void OpenExtensionSettingsWindow(string extensionPath);
        bool ShowConfirmation(string message, string title, bool isWarning = false);
        void ShowWarning(string message, string title);
        void ShowInformation(string message, string title);
    }
}