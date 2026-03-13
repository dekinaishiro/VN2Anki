using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace VN2Anki.Helpers
{
    public static class ImageHelper
    {
        public static BitmapImage? BytesToBitmap(byte[]? bytes, int decodeWidth = 0)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try
            {
                var image = new BitmapImage();
                using (var mem = new MemoryStream(bytes))
                {
                    mem.Position = 0;
                    image.BeginInit();
                    image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = mem;
                    if (decodeWidth > 0)
                    {
                        image.DecodePixelWidth = decodeWidth;
                    }
                    image.EndInit();
                }
                image.Freeze();
                return image;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error converting bytes to Bitmap: {ex.Message}");
                return null;
            }
        }
    }
}