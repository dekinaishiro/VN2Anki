namespace VN2Anki.Services.Interfaces
{
    public interface IHotkeyService
    {
        void Initialize(System.Windows.Interop.HwndSource hwndSource);
        void ReloadHotkeys();
    }
}