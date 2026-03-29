using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Extensions.Logging;
using VN2Anki.Services.Interfaces;
using CommunityToolkit.Mvvm.Messaging;
using VN2Anki.Messages;

namespace VN2Anki.Services
{
    public class InputMonitorService : IInputMonitorService, IRecipient<BufferStartedMessage>, IRecipient<BufferStoppedMessage>, IRecipient<SessionEndedMessage>
    {
        private readonly ISessionLoggerService _sessionLogger;
        private readonly IConfigurationService _configService;
        private readonly ILogger<InputMonitorService> _logger;

        private IntPtr _mouseHookId = IntPtr.Zero;
        private IntPtr _keyboardHookId = IntPtr.Zero;
        
        private Win32InteropService.HookProc _mouseProc;
        private Win32InteropService.HookProc _keyboardProc;
        
        private bool _isMonitoring;

        public InputMonitorService(
            ISessionLoggerService sessionLogger,
            IConfigurationService configService,
            ILogger<InputMonitorService> logger)
        {
            _sessionLogger = sessionLogger;
            _configService = configService;
            _logger = logger;
            
            _mouseProc = MouseHookCallback;
            _keyboardProc = KeyboardHookCallback;

            WeakReferenceMessenger.Default.RegisterAll(this);
        }

        public void Receive(BufferStartedMessage message) => Start();
        public void Receive(BufferStoppedMessage message) => Stop();
        public void Receive(SessionEndedMessage message) => Stop();

        public void Start()
        {
            if (_isMonitoring) return;

            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                IntPtr hMod = Win32InteropService.GetModuleHandle(curModule.ModuleName);
                _mouseHookId = Win32InteropService.SetWindowsHookEx(Win32InteropService.WH_MOUSE_LL, _mouseProc, hMod, 0);
                _keyboardHookId = Win32InteropService.SetWindowsHookEx(Win32InteropService.WH_KEYBOARD_LL, _keyboardProc, hMod, 0);
            }

            _isMonitoring = _mouseHookId != IntPtr.Zero || _keyboardHookId != IntPtr.Zero;

            if (_isMonitoring) _logger.LogInformation("InputMonitorService started.");
            else _logger.LogError("Failed to start InputMonitorService hooks.");
        }

        public void Stop()
        {
            if (!_isMonitoring) return;

            if (_mouseHookId != IntPtr.Zero)
            {
                Win32InteropService.UnhookWindowsHookEx(_mouseHookId);
                _mouseHookId = IntPtr.Zero;
            }
            if (_keyboardHookId != IntPtr.Zero)
            {
                Win32InteropService.UnhookWindowsHookEx(_keyboardHookId);
                _keyboardHookId = IntPtr.Zero;
            }

            _isMonitoring = false;
            _logger.LogInformation("InputMonitorService stopped.");
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)Win32InteropService.WM_LBUTTONDOWN || wParam == (IntPtr)Win32InteropService.WM_RBUTTONDOWN))
            {
                string target = GetInputTarget();
                if (target != "external")
                {
                    string btn = wParam == (IntPtr)Win32InteropService.WM_LBUTTONDOWN ? "left" : "right";
                    _ = _sessionLogger.LogEventAsync("CLICK", new { source = "mouse", button = btn, target });
                }
            }
            return Win32InteropService.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)Win32InteropService.WM_KEYDOWN || wParam == (IntPtr)Win32InteropService.WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);
                // Enter = 13, Space = 32, PageDown = 34
                if (vkCode == 13 || vkCode == 32 || vkCode == 34) 
                {
                    string target = GetInputTarget();
                    if (target != "external")
                    {
                        _ = _sessionLogger.LogEventAsync("CLICK", new { source = "keyboard", key = vkCode, target });
                    }
                }
            }
            return Win32InteropService.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        private string GetInputTarget()
        {
            IntPtr hwnd = Win32InteropService.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "external";

            // 1. Is it our App?
            string appWindowState = null;
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (Application.Current != null)
                    {
                        foreach (Window window in Application.Current.Windows)
                        {
                            var helper = new System.Windows.Interop.WindowInteropHelper(window);
                            if (helper.Handle == hwnd)
                            {
                                appWindowState = window.GetType().Name.ToLower().Replace("window", "");
                                break;
                            }
                        }
                    }
                });
            }
            catch { }

            if (appWindowState != null) return appWindowState;

            // 2. Is it the Game?
            Win32InteropService.GetWindowThreadProcessId(hwnd, out uint foregroundPid);
            var gameWindowName = _configService?.CurrentConfig?.Media?.VideoWindow;

            if (!string.IsNullOrEmpty(gameWindowName))
            {
                try
                {
                    var gameProcesses = Process.GetProcessesByName(gameWindowName);
                    bool isGameInFocus = gameProcesses.Any(p => (uint)p.Id == foregroundPid);
                    foreach (var p in gameProcesses) p.Dispose();

                    if (isGameInFocus) return "game";
                }
                catch { }
            }

            return "external";
        }

        public void Dispose()
        {
            Stop();
        }
    }
}