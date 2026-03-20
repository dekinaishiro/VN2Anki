// E:\Coding\VN2Anki\VN2Anki\Services\UserActivityService.cs
using System;
using System.Timers;
using Microsoft.Extensions.Logging;
using VN2Anki.Services.Interfaces;
using CommunityToolkit.Mvvm.Messaging;
using VN2Anki.Messages;
using Timer = System.Timers.Timer;

namespace VN2Anki.Services
{
    public class UserActivityService : IUserActivityService, IDisposable, IRecipient<BufferStartedMessage>, IRecipient<BufferStoppedMessage>, IRecipient<SessionEndedMessage>
    {
        private readonly ISessionLoggerService _sessionLogger;
        private readonly ILogger<UserActivityService> _logger;
        private readonly Timer _timer;

        private POINT _lastMousePos;
        private bool _isStarted;

        public UserActivityService(
            ISessionLoggerService sessionLogger,
            ILogger<UserActivityService> logger)
        {
            _sessionLogger = sessionLogger;
            _logger = logger;

            _timer = new Timer(10000); // Heartbeat a cada 10 segundos
            _timer.Elapsed += OnTimerElapsed;

            WeakReferenceMessenger.Default.RegisterAll(this);
        }

        public void Receive(BufferStartedMessage message) => Start();
        public void Receive(BufferStoppedMessage message) => Stop();
        public void Receive(SessionEndedMessage message) => Stop();

        public void Start()
        {
            if (_isStarted) return;
            Win32InteropService.GetCursorPos(out _lastMousePos); // Reseta a posição inicial
            _timer.Start();
            _isStarted = true;
            _logger.LogInformation("UserActivityService (Heartbeat) started.");
        }

        public void Stop()
        {
            if (!_isStarted) return;
            _timer.Stop();
            _isStarted = false;
            _logger.LogInformation("UserActivityService (Heartbeat) stopped.");
        }

        private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            try
            {
                if (CheckActivity())
                {
                    _ = _sessionLogger.LogEventAsync("HEARTBEAT", new { activity = true });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during activity heartbeat check.");
            }
        }

        private bool CheckActivity()
        {
            // 1. Checa movimento do Mouse
            Win32InteropService.GetCursorPos(out POINT currentMousePos);
            bool mouseMoved = currentMousePos.X != _lastMousePos.X || currentMousePos.Y != _lastMousePos.Y;
            _lastMousePos = currentMousePos;

            if (mouseMoved) return true;

            // 2. Checa Teclas
            int[] keysToCheck = {
                0x01, 0x02, // Mouse L/R
                0x0D, 0x20, // Enter, Space
                0x25, 0x26, 0x27, 0x28, // Arrows
                0x41, 0x53, 0x44, 0x57, // WASD
                0x11, 0x10, 0x12, // Ctrl, Shift, Alt
                0x09 // Tab
            };

            foreach (var key in keysToCheck)
            {
                if ((Win32InteropService.GetAsyncKeyState(key) & 0x8000) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}