// E:\Coding\VN2Anki\VN2Anki\Services\WindowFocusMonitorService.cs
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using VN2Anki.Services.Interfaces;
using CommunityToolkit.Mvvm.Messaging;
using VN2Anki.Messages;

namespace VN2Anki.Services
{
    public class WindowFocusMonitorService : IWindowFocusMonitorService, IDisposable, IRecipient<BufferStartedMessage>, IRecipient<BufferStoppedMessage>, IRecipient<SessionEndedMessage>
    {
        private readonly IConfigurationService _configService;
        private readonly ISessionLoggerService _sessionLogger;
        private readonly ILogger<WindowFocusMonitorService> _logger;

        private IntPtr _hHook = IntPtr.Zero;
        private Win32InteropService.WinEventDelegate _hookDelegate;
        private bool _isMonitoring;

        public WindowFocusMonitorService(
            IConfigurationService configService,
            ISessionLoggerService sessionLogger,
            ILogger<WindowFocusMonitorService> logger)
        {
            _configService = configService;
            _sessionLogger = sessionLogger;
            _logger = logger;

            // prevents hook delegate from being garbage collected
            _hookDelegate = new Win32InteropService.WinEventDelegate(WinEventProc);

            WeakReferenceMessenger.Default.RegisterAll(this);
        }

        public void Receive(BufferStartedMessage message) => Start();
        public void Receive(BufferStoppedMessage message) => Stop();
        public void Receive(SessionEndedMessage message) => Stop();

        public void Start()
        {
            if (_isMonitoring) return;

            _hHook = Win32InteropService.SetWinEventHook(
                Win32InteropService.EVENT_SYSTEM_FOREGROUND,
                Win32InteropService.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _hookDelegate,
                0, 0,
                Win32InteropService.WINEVENT_OUTOFCONTEXT);

            _isMonitoring = _hHook != IntPtr.Zero;

            if (_isMonitoring) _logger.LogInformation("WindowFocusMonitorService started.");
            else _logger.LogError("Failed to start WindowFocusMonitorService.");
        }

        public void Stop()
        {
            if (!_isMonitoring) return;

            Win32InteropService.UnhookWinEvent(_hHook);
            _hHook = IntPtr.Zero;
            _isMonitoring = false;

            _logger.LogInformation("WindowFocusMonitorService stopped.");
        }

        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (hwnd == IntPtr.Zero || eventType != Win32InteropService.EVENT_SYSTEM_FOREGROUND) return;

            string focusState = DetermineFocusState(hwnd);
            _ = _sessionLogger.LogEventAsync("APP_STATE", new { focus = focusState });
        }

        private string DetermineFocusState(IntPtr hwnd)
        {
            // Proteção 1: O aplicativo está no meio do processo de fechamento?
            if (Application.Current == null || Application.Current.Dispatcher == null || Application.Current.Dispatcher.HasShutdownStarted)
            {
                return "external";
            }

            // 1. É o nosso App?
            string appWindowState = null;
            
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // Proteção 2: Validação final dentro da thread da UI
                    if (Application.Current == null) return;

                    foreach (Window window in Application.Current.Windows)
                    {
                        var helper = new WindowInteropHelper(window);
                        if (helper.Handle == hwnd)
                        {
                            // Exemplo: De "OverlayWindow" para "overlay"
                            appWindowState = window.GetType().Name.ToLower().Replace("window", "");
                            break;
                        }
                    }
                });
            }
            catch (Exception)
            {
                // Se a invoke falhar (ex: a task foi cancelada pela morte da thread UI), ignoramos.
                return "external";
            }

            if (appWindowState != null)
            {
                return appWindowState; 
            }

            // 2. É o Jogo?
            Win32InteropService.GetWindowThreadProcessId(hwnd, out uint foregroundPid);
            
            // Adicionado operador "?." para proteger o acesso caso o serviço de config já tenha sido descartado
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
                catch 
                {
                    // GetProcessesByName pode estourar exceção durante o shutdown dependendo do contexto.
                }
            }

            // 3. Mundo Exterior
            return "external";
        }

        public void Dispose()
        {
            Stop();
        }
    }
}