using System;

namespace VN2Anki.Services.Interfaces
{
    public interface IBridgeService : IDisposable
    {
        string ActiveHoverSlotId { get; set; }
        void Restart(int newPort);
    }
}
