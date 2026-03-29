using System;

namespace VN2Anki.Services.Interfaces
{
    public interface IInputMonitorService : IDisposable
    {
        void Start();
        void Stop();
    }
}