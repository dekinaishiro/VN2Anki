using System;
using System.Threading.Tasks;

namespace VN2Anki.Services.Interfaces
{
    public interface ISessionLoggerService
    {
        string CurrentLogPath { get; }
        string SessionId { get; }
        event EventHandler<string> OnLogWritten;
        
        Task StartNewSessionAsync();
        Task LogEventAsync(string eventType, object data);
        Task EndSessionAsync(bool discard = false);
    }
}
