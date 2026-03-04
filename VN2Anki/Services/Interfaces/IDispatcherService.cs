using System;

namespace VN2Anki.Services.Interfaces
{
    public interface IDispatcherService
    {
        void Invoke(Action action);
    }
}