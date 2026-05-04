using System;
using System.Collections.Generic;
using System.Windows.Interop;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using VN2Anki.Messages;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.Services
{
    public class HotkeyService : IHotkeyService, IDisposable
    {
        private readonly IConfigurationService _configService;
        private readonly ILogger<HotkeyService> _logger;
        private HwndSource _hwndSource;
        private Dictionary<int, string> _registeredHotkeys = new();
        private int _currentId = 9000;
        private const int WM_HOTKEY = 0x0312;

        public HotkeyService(IConfigurationService configService, ILogger<HotkeyService> logger)
        {
            _configService = configService;
            _logger = logger;
        }

        public void Initialize(HwndSource hwndSource)
        {
            if (_hwndSource != null) return;
            
            _hwndSource = hwndSource;
            _hwndSource.AddHook(HwndHook);
            
            ReloadHotkeys();
        }

        public void ReloadHotkeys()
        {
            UnregisterAll();

            var config = _configService.CurrentConfig;
            if (config.Hotkeys == null) return;

            foreach (var hk in config.Hotkeys)
            {
                if (!hk.IsEnabled || hk.Key == 0) continue;

                int id = _currentId++;
                bool success = Win32InteropService.RegisterHotKey(_hwndSource.Handle, id, (uint)hk.Modifiers, (uint)hk.Key);
                
                if (success)
                {
                    _registeredHotkeys.Add(id, hk.ActionName);
                    _logger.LogInformation($"Registered hotkey {hk.ActionName} with ID {id}");
                }
                else
                {
                    _logger.LogWarning($"Failed to register hotkey {hk.ActionName}");
                }
            }
        }

        private void UnregisterAll()
        {
            if (_hwndSource == null) return;

            foreach (var id in _registeredHotkeys.Keys)
            {
                Win32InteropService.UnregisterHotKey(_hwndSource.Handle, id);
            }
            _registeredHotkeys.Clear();
            _currentId = 9000; // Reset ID counter
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (_registeredHotkeys.TryGetValue(id, out string actionName))
                {
                    _logger.LogInformation($"Hotkey triggered: {actionName}");
                    WeakReferenceMessenger.Default.Send(new HotkeyActionMessage(actionName));
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            UnregisterAll();
            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(HwndHook);
                _hwndSource = null;
            }
        }
    }
}
