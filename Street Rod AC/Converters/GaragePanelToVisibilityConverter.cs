using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Street_Rod_AC.Screens.Garage;

namespace Street_Rod_AC.Converters
{
    /// <summary>
    /// Converts a GaragePanel value to Visibility based on a parameter.
    /// Usage: Visibility="{Binding ActivePanel, Converter={StaticResource GaragePanelToVisibilityConverter}, ConverterParameter=Calendar}"
    /// </summary>
    public class GaragePanelToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not GaragePanel currentPanel)
                return Visibility.Collapsed;

            if (parameter is not string paramString)
                return Visibility.Collapsed;

            // Parse the parameter as a GaragePanel enum value
            if (Enum.TryParse<GaragePanel>(paramString, true, out var targetPanel))
            {
                return currentPanel == targetPanel ? Visibility.Visible : Visibility.Collapsed;
            }

            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
