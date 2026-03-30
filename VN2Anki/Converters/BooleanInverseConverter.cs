using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VN2Anki.Converters
{
    public class BooleanInverseConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isTrue = false;
            
            if (value is bool b) isTrue = b;
            else if (value is int i) isTrue = i > 0;

            bool inverted = !isTrue;

            if (targetType == typeof(Visibility))
            {
                return inverted ? Visibility.Visible : Visibility.Collapsed;
            }

            return inverted;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}