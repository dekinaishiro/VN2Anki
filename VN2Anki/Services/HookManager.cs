using System;

namespace VN2Anki.Services
{
    public class HookManager : ITextHook
    {
        //public event Action<string, DateTime> OnTextCopied;

        private readonly ClipboardHook _clipboardHook;
        private readonly WebsocketHook _websocketHook;
        private readonly IConfigurationService _configService;

        public HookManager(ClipboardHook clipboardHook, WebsocketHook websocketHook, IConfigurationService configService)
        {
            _clipboardHook = clipboardHook;
            _websocketHook = websocketHook;
            _configService = configService;

        }

        public void Start()
        {
            Stop();

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
            _clipboardHook.Stop();
            _websocketHook.Stop();
        }
    }
}