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
            // The one spec parser: "350hp @ 6000rpm" is 350, "1,200hp" is 1200, "335.5 bhp" is 335.5
            var bhp = Street_Rod_AC.Parts.Cars.AcSpecs.ParsePower(car.Specs?.Bhp);
            if (!bhp.HasValue)
                return false; // Cars without power data don't match power-based filters

            if (MinHP.HasValue && bhp.Value < MinHP.Value)
                return false;

            if (MaxHP.HasValue && bhp.Value > MaxHP.Value)
                return false;

            return true;
        }
    }
}
