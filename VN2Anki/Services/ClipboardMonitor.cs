using System;
using WK.Libraries.SharpClipboardNS;

namespace VN2Anki.Services
{
    public class ClipboardMonitor
    {
        private SharpClipboard _clipboard;
        public event Action<string, DateTime> OnTextCopied;
        private string _lastText = string.Empty;
        private DateTime _lastTime = DateTime.MinValue;
        private DateTime _startTime;

        private bool _isFirstEvent = true;      
        private bool _isListening = false;

        public ClipboardMonitor()
        {
            _clipboard = new SharpClipboard();
            _clipboard.ClipboardChanged += Clipboard_Changed;
        }

        public void Start()
        {
            _isFirstEvent = true;
            _isListening = true; 
            _startTime = DateTime.Now;
        }

        public void Stop()
        {
            _isListening = false;
        }

        private void Clipboard_Changed(object sender, SharpClipboard.ClipboardChangedEventArgs e)
        {
            
            if (!_isListening) return;

            if (_isFirstEvent)
            {
                _isFirstEvent = false;
                if (e.ContentType == SharpClipboard.ContentTypes.Text)
                    _lastText = e.Content?.ToString()?.Trim() ?? string.Empty;
                return;
            }

            if ((DateTime.Now - _startTime).TotalMilliseconds < 500) return;

            if (e.ContentType == SharpClipboard.ContentTypes.Text)
            {
                string text = e.Content?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (text == _lastText && (DateTime.Now - _lastTime).TotalMilliseconds < 1000) return;
                    _lastText = text;
                    _lastTime = DateTime.Now;
                    OnTextCopied?.Invoke(text, DateTime.Now);
                }
            }
        }
    }
}