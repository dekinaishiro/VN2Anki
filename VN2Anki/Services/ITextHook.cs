using System;

namespace VN2Anki.Services
{
    /// <summary>
    /// Contract for all text capture methods (Clipboard, WebSocket, etc.)
    /// </summary>
    public interface ITextHook
    {
        event Action<string, DateTime> OnTextCopied;
        void Start();
        void Stop();
    }
}