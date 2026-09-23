using System.Globalization;
using System.Windows.Data;

namespace Street_Rod_AC.Converters
{
    /// <summary>
    /// True when the values handed to it are the same object. For marking the one item of a list that is
    /// also being shown somewhere else.
    /// </summary>
    public class EqualityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2) return false;
            return ReferenceEquals(values[0], values[1]);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
