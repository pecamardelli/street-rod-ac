using System.Globalization;
using System.Windows.Data;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Street_Rod_AC.Screens.Diner;

namespace Street_Rod_AC.Converters
{
    /// <summary>
    /// An opponent's difficulty as a brush. The brushes are set where the converter is declared, so the colours
    /// live in the view and the view model only says how tough the opponent is.
    /// Usage: <c>&lt;converters:DifficultyToBrushConverter x:Key="..." Easy="..." Matched="..." Hard="..." Other="..."/&gt;</c>
    /// </summary>
    public class DifficultyToBrushConverter : IValueConverter
    {
        public Brush Easy { get; set; } = Brushes.LightGreen;
        public Brush Matched { get; set; } = Brushes.Gold;
        public Brush Hard { get; set; } = Brushes.IndianRed;

        /// <summary>For no opponent or a value that is not a difficulty</summary>
        public Brush Other { get; set; } = Brushes.White;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
        {
            OpponentDifficulty.Easy => Easy,
            OpponentDifficulty.Matched => Matched,
            OpponentDifficulty.Hard => Hard,
            _ => Other
        };

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
