using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VN2Anki.Services.Interfaces;
using CommunityToolkit.Mvvm.Messaging;
using VN2Anki.Messages;

namespace VN2Anki.Services
{
    public class SessionLoggerService : ISessionLoggerService, IDisposable
    {
        private string _currentLogPath = string.Empty;
        private StreamWriter _writer = null;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private string _sessionId = string.Empty;
        private readonly JsonSerializerOptions _jsonOptions;

        public string CurrentLogPath => _currentLogPath;
        public string SessionId => _sessionId;

        public event EventHandler<string> OnLogWritten;

        public SessionLoggerService()
        {
            _jsonOptions = new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = false
            };

            // Register to global messages for automatic logging
            WeakReferenceMessenger.Default.Register<TextCopiedMessage>(this, (r, m) =>
            {
                _ = LogEventAsync("HOOK", new { text = m.Text, length = m.Text.Length });
            });

            // Note: We could register for more messages here. 
            // BufferStarted/Stopped are handled by SessionManagerService calling StartNewSessionAsync/EndSessionAsync
            WeakReferenceMessenger.Default.Register<BufferStartedMessage>(this, (r, m) =>
            {
                _ = LogEventAsync("APP_STATE", new { state = "BUFFER_STARTED" });
            });
            
            WeakReferenceMessenger.Default.Register<BufferStoppedMessage>(this, (r, m) =>
            {
                _ = LogEventAsync("APP_STATE", new { state = "BUFFER_STOPPED" });
            });
        }

        public async Task StartNewSessionAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                CloseCurrentSession();

                _sessionId = Guid.NewGuid().ToString("N");
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var now = DateTime.UtcNow;
                var dir = Path.Combine(appData, "VN2Anki", "Sessions", now.Year.ToString(), now.Month.ToString("D2"));
                
                Directory.CreateDirectory(dir);
                _currentLogPath = Path.Combine(dir, $"session_{_sessionId}.jsonl");
                
                var fileStream = new FileStream(_currentLogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                _writer = new StreamWriter(fileStream) { AutoFlush = true };

                await LogEventInternalAsync("SESSION_START", new { sessionId = _sessionId });
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task LogEventAsync(string eventType, object data)
        {
            await _semaphore.WaitAsync();
            try
            {
                if (_writer == null) return;
                await LogEventInternalAsync(eventType, data);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task LogEventInternalAsync(string eventType, object data)
        {
            if (_writer == null) return;

            var logEntry = new
            {
                t = DateTime.UtcNow.ToString("O"),
                e = eventType,
                d = data
            };

            var json = JsonSerializer.Serialize(logEntry, _jsonOptions);
            await _writer.WriteLineAsync(json);
            
            OnLogWritten?.Invoke(this, json);
        }

        public async Task EndSessionAsync(bool discard = false)
        {
            await _semaphore.WaitAsync();
            try
            {
                if (_writer != null)
                {
                    await LogEventInternalAsync("SESSION_END", new { discarded = discard });
                    CloseCurrentSession();

                    if (discard && File.Exists(_currentLogPath))
                    {
                        try { File.Delete(_currentLogPath); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to delete session log: {ex.Message}"); }
                    }
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private void CloseCurrentSession()
        {
            if (_writer != null)
            {
                _writer.Dispose();
                _writer = null;
            }
        }

        public void Dispose()
        {
            WeakReferenceMessenger.Default.UnregisterAll(this);
            CloseCurrentSession();
            _semaphore.Dispose();
        }
    }
}
