using System;

namespace VN2Anki.Services
{
    public class HookManager : ITextHook
    {
        public event Action<string, DateTime> OnTextCopied;

        private readonly ClipboardHook _clipboardHook;
        private readonly WebsocketHook _websocketHook;
        private readonly IConfigurationService _configService;

        private bool _isRunning;

        public HookManager(ClipboardHook clipboardHook, WebsocketHook websocketHook, IConfigurationService configService)
        {
            _clipboardHook = clipboardHook;
            _websocketHook = websocketHook;
            _configService = configService;

            // Route both events to the main output
            _clipboardHook.OnTextCopied += (text, time) => OnTextCopied?.Invoke(text, time);
            _websocketHook.OnTextCopied += (text, time) => OnTextCopied?.Invoke(text, time);
        }

        public void Start()
        {
            Stop();
            _isRunning = true;

            int hookType = _configService.CurrentConfig.Hook.ActiveHookType;

            if (hookType == 0)
            {
                _clipboardHook.Start();
            }
            else
            {
                _websocketHook.Start();
            }
        }

        public void Stop()
        {
            _isRunning = false;
            _clipboardHook.Stop();
            _websocketHook.Stop();
        }
    }
}