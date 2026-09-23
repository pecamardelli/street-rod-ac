using System.Globalization;
using System.Windows.Data;

namespace Street_Rod_AC.Converters
{
    /// <summary>
    /// The opposite of a bool, still a bool. For binding "show this while that is not ready" to something
    /// that wants a bool rather than a Visibility.
    /// </summary>
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : true;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : false;
    }
}
