using System;

namespace VN2Anki.Services
{
    public class HookManager : ITextHook
    {
        //public event Action<string, DateTime> OnTextCopied;

        private readonly ClipboardHook _clipboardHook;
        private readonly WebsocketHook _websocketHook;
        private readonly MpvHook _mpvHook;
        private readonly IConfigurationService _configService;

        public HookManager(ClipboardHook clipboardHook, WebsocketHook websocketHook, MpvHook mpvHook, IConfigurationService configService)
        {
            _clipboardHook = clipboardHook;
            _websocketHook = websocketHook;
            _mpvHook = mpvHook;
            _configService = configService;

        }

        public void Start()
        {
            Stop();

            int hookType = _configService.CurrentConfig.Hook.ActiveHookType;

            switch (hookType)
            {
                case 0:
                    _clipboardHook.Start();
                    break;
                case 1:
                case 2:
                    _websocketHook.Start();
                    break;
                case 3:
                    _mpvHook.Start();
                    break;
            }
        }

        public void Stop()
        {
            _clipboardHook.Stop();
            _websocketHook.Stop();
            _mpvHook.Stop();
        }
    }
}