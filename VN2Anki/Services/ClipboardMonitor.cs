using System;
using System.Threading.Tasks;
using System.Windows;
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

        private async void Clipboard_Changed(object sender, SharpClipboard.ClipboardChangedEventArgs e)
        {
            if (!_isListening) return;

            if (_isFirstEvent)
            {
                _isFirstEvent = false;
                _lastText = await GetClipboardTextSafeAsync(); // gets initial state
                return;
            }

            if ((DateTime.Now - _startTime).TotalMilliseconds < 500) return;

            string text = await GetClipboardTextSafeAsync();

            if (!string.IsNullOrWhiteSpace(text))
            {
                if (text == _lastText && (DateTime.Now - _lastTime).TotalMilliseconds < 1000) return;

                _lastText = text;
                _lastTime = DateTime.Now;
                OnTextCopied?.Invoke(text, DateTime.Now);
            }
        }

        private async Task<string> GetClipboardTextSafeAsync(int maxRetries = 5, int delayMs = 50)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                string text = null;
                bool success = false;

                // Dispatcher since clipboard uses sta
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        if (Clipboard.ContainsText())
                        {
                            text = Clipboard.GetText();
                        }
                        success = true; 
                    }
                    catch (System.Runtime.InteropServices.COMException)
                    {
                        // other process is using the clipboard
                        success = false;
                    }
                    catch (Exception)
                    {
                        // generic error
                        success = false;
                    }
                });

                if (success)
                {
                    return text?.Trim() ?? string.Empty;
                }

                await Task.Delay(delayMs);
            }

            // avoid endless loop
            return string.Empty;
        }
    }
}