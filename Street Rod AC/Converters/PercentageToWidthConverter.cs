using System.Globalization;
using System.Windows.Data;

namespace Street_Rod_AC.Converters
{
    /// <summary>
    /// Converts a percentage (0-100) and a container width to a pixel width.
    /// Used for progress bars.
    /// </summary>
    public class PercentageToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2)
                return 0.0;

            // First value is percentage (0-100)
            // Second value is container width
            if (values[0] is float percentage && values[1] is double containerWidth)
            {
                // Subtract some margin for the border
                var availableWidth = containerWidth - 4;
                return Math.Max(0, (percentage / 100.0) * availableWidth);
            }

            if (values[0] is double percentageDouble && values[1] is double containerWidth2)
            {
                var availableWidth = containerWidth2 - 4;
                return Math.Max(0, (percentageDouble / 100.0) * availableWidth);
            }

            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
