using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace VN2Anki.Models
{
    public class MiningSlot : INotifyPropertyChanged, IDisposable
    {
        public string Id { get; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Text { get; set; }
        public DateTime Timestamp { get; set; }

        private byte[] _audioBytes;
        public byte[] AudioBytes
        {
            get => _audioBytes;
            set
            {
                _audioBytes = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsOpen)); 
            }
        }

        private byte[] _screenshotBytes;
        public byte[] ScreenshotBytes
        {
            get => _screenshotBytes;
            set
            {
                _screenshotBytes = value;
                _thumbnail = null; // null the cached thumbnail so it will be regenerated with the new screenshot bytes when requested
                
                SaveScreenshotToDisk();

                OnPropertyChanged();
                OnPropertyChanged(nameof(Thumbnail));
                OnPropertyChanged(nameof(ScreenshotUrl));
            }
        }
        
        public string ScreenshotFilePath { get; private set; }
        public string ScreenshotUrl => string.IsNullOrEmpty(ScreenshotFilePath) ? null : $"http://vn.local/thumbs/{Path.GetFileName(ScreenshotFilePath)}";

        private void SaveScreenshotToDisk()
        {
            if (_screenshotBytes == null || _screenshotBytes.Length == 0) return;

            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string thumbsDir = Path.Combine(appData, "VN2Anki", "thumbs");
                if (!Directory.Exists(thumbsDir)) Directory.CreateDirectory(thumbsDir);

                string filePath = Path.Combine(thumbsDir, $"{Id}.jpg");
                File.WriteAllBytes(filePath, _screenshotBytes);
                ScreenshotFilePath = filePath;
            }
            catch { /* Ignore IO errors */ }
        }

        public string DisplayTime => Timestamp.ToString("HH:mm:ss");

        // this property is used to determine if the slot is still open (i.e. audio is still being recorded) or if it has been sealed with audio data, which affects how it's displayed in the UI and whether it can be mined to Anki
        public bool IsOpen => AudioBytes == null;

        private BitmapImage _thumbnail;

        public BitmapImage Thumbnail
        {
            get
            {
                // returns a cached thumbnail if available, otherwise creates it from the screenshot bytes and caches it for future use
                if (_thumbnail != null) return _thumbnail;

                if (ScreenshotBytes == null || ScreenshotBytes.Length == 0) return null;
                _thumbnail = VN2Anki.Helpers.ImageHelper.BytesToBitmap(ScreenshotBytes, 150);
                return _thumbnail;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        
        public void Dispose()
        {
            // remove refs for gc
            AudioBytes = null;
            ScreenshotBytes = null;
            _thumbnail = null;

            if (!string.IsNullOrEmpty(ScreenshotFilePath) && File.Exists(ScreenshotFilePath))
            {
                try { File.Delete(ScreenshotFilePath); } catch { }
            }

            GC.SuppressFinalize(this);
        }
        public void ClearMedia() => Dispose();
    }
}