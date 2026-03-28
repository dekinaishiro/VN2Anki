using System.Collections.Generic;
using VN2Anki.Models.Entities;
using VN2Anki.Models;
namespace VN2Anki.Services.Interfaces
{
    public interface IWindowService
    {
        void CloseWindow(object viewModel, bool dialogResult);
        VisualNovel? ShowMultipleVnPrompt(List<VisualNovel> vns);
        void OpenExtensionSettingsWindow(string extensionPath, object? owner = null);
        void OpenExtensionsManager(object? owner = null);
        void OpenUserHub();
        void OpenSettings();
        void OpenOverlay();
        void OpenMiningHistory(System.Collections.IEnumerable history, Action<MiningSlot> onDelete);
        bool ShowConfirmation(string message, string title, bool isWarning = false);
        void ShowWarning(string message, string title);
        void ShowInformation(string message, string title);
    }
}