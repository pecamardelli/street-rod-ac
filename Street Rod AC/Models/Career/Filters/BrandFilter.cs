using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Models.Career.Filters
{
    /// <summary>
    /// Filter that matches cars by manufacturer/brand.
    /// Example: "Ford only", "Chevrolet or Pontiac"
    /// </summary>
    public class BrandFilter : ICarFilter
    {
        public string FilterType => "Brand";

        /// <summary>
        /// Brand name to match (case-insensitive)
        /// </summary>
        public string Brand { get; set; } = string.Empty;

        public string DisplayDescription => $"{Brand} only";

        public BrandFilter() { }

        public BrandFilter(string brand)
        {
            Brand = brand;
        }

        public bool Matches(CarDefinition car, Car? instance = null)
        {
            if (string.IsNullOrEmpty(Brand))
                return true;

            return string.Equals(car.Brand, Brand, StringComparison.OrdinalIgnoreCase);
        }
    }
}
