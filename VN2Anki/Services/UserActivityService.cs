using System;
using System.Diagnostics;
using System.Linq;
using System.Timers;
using Microsoft.Extensions.Logging;
using VN2Anki.Services.Interfaces;
using Timer = System.Timers.Timer;

namespace VN2Anki.Services
{
    public class UserActivityService : IUserActivityService, IDisposable
    {
        private readonly IConfigurationService _configService;
        private readonly ISessionLoggerService _sessionLogger;
        private readonly ILogger<UserActivityService> _logger;
        private readonly Timer _timer;
        
        private POINT _lastMousePos;
        private bool _isStarted;

        public UserActivityService(
            IConfigurationService configService,
            ISessionLoggerService sessionLogger,
            ILogger<UserActivityService> logger)
        {
            _configService = configService;
            _sessionLogger = sessionLogger;
            _logger = logger;

            _timer = new Timer(10000); // Check every 10 seconds
            _timer.Elapsed += OnTimerElapsed;
        }

        public void Start()
        {
            if (_isStarted) return;
            _timer.Start();
            _isStarted = true;
            _logger.LogInformation("UserActivityService started.");
        }

        public void Stop()
        {
            if (!_isStarted) return;
            _timer.Stop();
            _isStarted = false;
            _logger.LogInformation("UserActivityService stopped.");
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
                _logger.LogWarning(ex, "Error during activity check.");
            }
        }

        private bool CheckActivity()
        {
            // 1. Identify Foreground Window
            IntPtr foregroundWindow = Win32InteropService.GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero) return false;

            Win32InteropService.GetWindowThreadProcessId(foregroundWindow, out uint foregroundPid);
            uint currentAppPid = (uint)Process.GetCurrentProcess().Id;

            // 2. Identify if it's the Game or our App
            bool isGameOrApp = false;
            if (foregroundPid == currentAppPid)
            {
                isGameOrApp = true;
            }
            else
            {
                var gameWindowName = _configService.CurrentConfig.Media.VideoWindow;
                if (!string.IsNullOrEmpty(gameWindowName))
                {
                    var gameProcesses = Process.GetProcessesByName(gameWindowName);
                    isGameOrApp = gameProcesses.Any(p => (uint)p.Id == foregroundPid);
                    foreach (var p in gameProcesses) p.Dispose();
                }
            }

            if (!isGameOrApp) return false;

            // 3. Check for Mouse Movement
            Win32InteropService.GetCursorPos(out POINT currentMousePos);
            bool mouseMoved = currentMousePos.X != _lastMousePos.X || currentMousePos.Y != _lastMousePos.Y;
            _lastMousePos = currentMousePos;

            if (mouseMoved) return true;

            // 4. Check for Key Presses (Common keys for VNs/Mining)
            // Left Mouse, Right Mouse, Enter, Space, Arrows, WASD, Ctrl, Shift, Alt
            int[] keysToCheck = { 
                0x01, 0x02, // Mouse
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
