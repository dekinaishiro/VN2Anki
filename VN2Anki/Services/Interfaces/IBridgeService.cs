using System;

namespace VN2Anki.Services.Interfaces
{
    public interface IBridgeService : IDisposable
    {
        void Restart(int newPort);
    }
}
