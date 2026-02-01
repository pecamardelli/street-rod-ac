using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Models.Career.Filters
{
    /// <summary>
    /// Filter that matches cars by year range/decade.
    /// Example: "1950s classics", "Pre-war vehicles"
    /// </summary>
    public class DecadeFilter : ICarFilter
    {
        public string FilterType => "Decade";

        /// <summary>
        /// Minimum year (inclusive). Null means no minimum.
        /// </summary>
        public int? StartYear { get; set; }

        /// <summary>
        /// Maximum year (inclusive). Null means no maximum.
        /// </summary>
        public int? EndYear { get; set; }

        public string DisplayDescription
        {
            get
            {
                if (StartYear.HasValue && EndYear.HasValue)
                {
                    if (EndYear.Value - StartYear.Value == 9 && StartYear.Value % 10 == 0)
                        return $"{StartYear.Value}s";
                    return $"{StartYear.Value}-{EndYear.Value}";
                }
                if (StartYear.HasValue)
                    return $"{StartYear.Value} or newer";
                if (EndYear.HasValue)
                    return $"{EndYear.Value} or older";
                return "Any year";
            }
        }

        public DecadeFilter() { }

        public DecadeFilter(int? startYear, int? endYear)
        {
            StartYear = startYear;
            EndYear = endYear;
        }

        /// <summary>
        /// Creates a filter for a specific decade (e.g., 1960 for 1960s)
        /// </summary>
        public static DecadeFilter ForDecade(int decadeStart)
        {
            return new DecadeFilter(decadeStart, decadeStart + 9);
        }

        public bool Matches(CarDefinition car, Car? instance = null)
        {
            if (!car.Year.HasValue)
                return false; // Cars without year data don't match year-based filters

            if (StartYear.HasValue && car.Year.Value < StartYear.Value)
                return false;

            if (EndYear.HasValue && car.Year.Value > EndYear.Value)
                return false;

            return true;
        }
    }
}
