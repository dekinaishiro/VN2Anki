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

        public byte[] ScreenshotBytes { get; set; }
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
                try
                {
                    var image = new BitmapImage();
                    using (var mem = new MemoryStream(ScreenshotBytes))
                    {
                        mem.Position = 0;
                        image.BeginInit();
                        image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.UriSource = null;
                        image.StreamSource = mem;
                        image.DecodePixelWidth = 150;
                        image.EndInit();
                    }
                    image.Freeze();

                    _thumbnail = image; // store the generated thumbnail in the private field for future access
                    return _thumbnail;
                }
                catch { return null; }
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

            GC.SuppressFinalize(this);
        }
        public void ClearMedia() => Dispose();
    }
}