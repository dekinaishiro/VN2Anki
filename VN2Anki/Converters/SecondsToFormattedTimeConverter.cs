using System;
using System.Globalization;
using System.Windows.Data;

namespace VN2Anki.Converters
{
    public class SecondsToFormattedTimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int seconds)
            {
                var time = TimeSpan.FromSeconds(seconds);
                return time.ToString(@"hh\:mm\:ss");
            }
            return "00:00:00";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}