using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Models.Career.Filters
{
    /// <summary>
    /// Filter that matches cars by value/price range.
    /// Requires a car instance to check actual purchase price.
    /// Example: "Budget builds under $5000", "High-roller specials"
    /// </summary>
    public class ValueFilter : ICarFilter
    {
        public string FilterType => "Value";

        /// <summary>
        /// Minimum value (inclusive). Null means no minimum.
        /// </summary>
        public decimal? MinValue { get; set; }

        /// <summary>
        /// Maximum value (inclusive). Null means no maximum.
        /// </summary>
        public decimal? MaxValue { get; set; }

        public string DisplayDescription
        {
            get
            {
                if (MinValue.HasValue && MaxValue.HasValue)
                    return $"${MinValue:N0}-${MaxValue:N0} value";
                if (MinValue.HasValue)
                    return $"${MinValue:N0}+ value";
                if (MaxValue.HasValue)
                    return $"Under ${MaxValue:N0}";
                return "Any value";
            }
        }

        public ValueFilter() { }

        public ValueFilter(decimal? minValue, decimal? maxValue)
        {
            MinValue = minValue;
            MaxValue = maxValue;
        }

        public bool Matches(CarDefinition car, Car? instance = null)
        {
            // Value filter requires an instance to check purchase price
            if (instance == null)
                return false;

            var value = instance.PurchasePrice;

            if (MinValue.HasValue && value < MinValue.Value)
                return false;

            if (MaxValue.HasValue && value > MaxValue.Value)
                return false;

            return true;
        }
    }
}
