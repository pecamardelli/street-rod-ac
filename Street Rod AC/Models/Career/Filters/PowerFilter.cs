using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Models.Career.Filters
{
    /// <summary>
    /// Filter that matches cars by horsepower range.
    /// Example: "Under 200hp", "300+ HP monsters"
    /// </summary>
    public class PowerFilter : ICarFilter
    {
        public string FilterType => "Power";

        /// <summary>
        /// Minimum horsepower (inclusive). Null means no minimum.
        /// </summary>
        public int? MinHP { get; set; }

        /// <summary>
        /// Maximum horsepower (inclusive). Null means no maximum.
        /// </summary>
        public int? MaxHP { get; set; }

        public string DisplayDescription
        {
            get
            {
                if (MinHP.HasValue && MaxHP.HasValue)
                    return $"{MinHP}-{MaxHP} HP";
                if (MinHP.HasValue)
                    return $"{MinHP}+ HP";
                if (MaxHP.HasValue)
                    return $"Under {MaxHP} HP";
                return "Any power";
            }
        }

        public PowerFilter() { }

        public PowerFilter(int? minHP, int? maxHP)
        {
            MinHP = minHP;
            MaxHP = maxHP;
        }

        public bool Matches(CarDefinition car, Car? instance = null)
        {
            // Try to parse BHP from specs
            var bhp = ParseBhp(car.Specs?.Bhp);
            if (!bhp.HasValue)
                return false; // Cars without power data don't match power-based filters

            if (MinHP.HasValue && bhp.Value < MinHP.Value)
                return false;

            if (MaxHP.HasValue && bhp.Value > MaxHP.Value)
                return false;

            return true;
        }

        /// <summary>
        /// Parse BHP value from specs string (e.g., "450 bhp", "350hp @ 6000rpm")
        /// </summary>
        private static int? ParseBhp(string? bhpString)
        {
            if (string.IsNullOrWhiteSpace(bhpString))
                return null;

            // Remove common suffixes and extract first number
            var cleaned = bhpString
                .Replace("bhp", "", StringComparison.OrdinalIgnoreCase)
                .Replace("hp", "", StringComparison.OrdinalIgnoreCase)
                .Trim();

            // Find first contiguous number
            var numberChars = new List<char>();
            foreach (var c in cleaned)
            {
                if (char.IsDigit(c))
                    numberChars.Add(c);
                else if (numberChars.Count > 0)
                    break; // Stop at first non-digit after we have digits
            }

            if (numberChars.Count > 0 && int.TryParse(new string(numberChars.ToArray()), out var result))
                return result;

            return null;
        }
    }
}
