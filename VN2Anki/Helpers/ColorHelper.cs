using System;
using System.Windows.Media;

namespace VN2Anki.Helpers
{
    public static class ColorHelper
    {
        public static string WpfHexToCss(string hexColor)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hexColor);
                return $"rgba({color.R}, {color.G}, {color.B}, {(color.A / 255.0).ToString(System.Globalization.CultureInfo.InvariantCulture)})";
            }
            catch
            {
                return "rgba(0,0,0,0)";
            }
        }
    }
}