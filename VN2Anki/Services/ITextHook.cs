using System;

namespace VN2Anki.Services
{
    // Contract for all text capture methods (Clipboard, WebSocket, etc.)
    public interface ITextHook
    {
        event Action<string, DateTime> OnTextCopied;
        void Start();
        void Stop();
    }
}