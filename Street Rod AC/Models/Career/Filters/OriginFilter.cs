using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Models.Career.Filters
{
    /// <summary>
    /// Filter that matches cars by country/region of origin.
    /// Example: "American muscle", "Japanese imports", "European sports"
    /// </summary>
    public class OriginFilter : ICarFilter
    {
        public string FilterType => "Origin";

        /// <summary>
        /// Country or region to match (case-insensitive).
        /// Supports both country names ("USA", "Japan") and region aliases ("American", "Japanese").
        /// </summary>
        public string Origin { get; set; } = string.Empty;

        public string DisplayDescription => $"{Origin} cars";

        // Regional brand mappings for when country data is missing
        private static readonly Dictionary<string, HashSet<string>> RegionBrands = new(StringComparer.OrdinalIgnoreCase)
        {
            ["USA"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "Ford", "Chevrolet", "Dodge", "Plymouth", "Pontiac", "Buick",
                "Cadillac", "Lincoln", "Mercury", "Oldsmobile", "AMC", "Chrysler",
                "Jeep", "GMC", "Tesla", "Shelby", "Corvette"
            },
            ["Japan"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "Toyota", "Honda", "Nissan", "Mazda", "Mitsubishi", "Subaru",
                "Suzuki", "Daihatsu", "Lexus", "Infiniti", "Acura", "Isuzu"
            },
            ["Germany"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "BMW", "Mercedes", "Mercedes-Benz", "Audi", "Porsche", "Volkswagen",
                "Opel", "VW"
            },
            ["UK"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "Jaguar", "Aston Martin", "Bentley", "Rolls-Royce", "McLaren",
                "Lotus", "MG", "Mini", "Triumph", "Austin", "Land Rover"
            },
            ["Italy"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "Ferrari", "Lamborghini", "Maserati", "Alfa Romeo", "Fiat",
                "Lancia", "Pagani"
            },
            ["France"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "Renault", "Peugeot", "Citroen", "Bugatti", "Alpine"
            }
        };

        // Region aliases (e.g., "American" -> "USA")
        private static readonly Dictionary<string, string> RegionAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["American"] = "USA",
            ["US"] = "USA",
            ["United States"] = "USA",
            ["Japanese"] = "Japan",
            ["German"] = "Germany",
            ["British"] = "UK",
            ["English"] = "UK",
            ["Italian"] = "Italy",
            ["French"] = "France",
            ["European"] = "Europe"
        };

        // European countries for "European" region filter
        private static readonly HashSet<string> EuropeanCountries = new(StringComparer.OrdinalIgnoreCase)
        {
            "Germany", "UK", "Italy", "France", "Sweden", "Netherlands", "Spain", "Belgium"
        };

        public OriginFilter() { }

        public OriginFilter(string origin)
        {
            Origin = origin;
        }

        public bool Matches(CarDefinition car, Car? instance = null)
        {
            if (string.IsNullOrEmpty(Origin))
                return true;

            var normalizedOrigin = NormalizeOrigin(Origin);

            // First check if car has explicit country data
            if (!string.IsNullOrEmpty(car.Country))
            {
                var carCountry = NormalizeOrigin(car.Country);

                // Handle "European" as a region
                if (normalizedOrigin == "Europe")
                    return EuropeanCountries.Contains(carCountry);

                return string.Equals(carCountry, normalizedOrigin, StringComparison.OrdinalIgnoreCase);
            }

            // Fall back to brand-based detection
            if (string.IsNullOrEmpty(car.Brand))
                return false;

            // Handle "European" as a region
            if (normalizedOrigin == "Europe")
            {
                foreach (var europeanCountry in EuropeanCountries)
                {
                    if (RegionBrands.TryGetValue(europeanCountry, out var brands) &&
                        brands.Contains(car.Brand))
                        return true;
                }
                return false;
            }

            // Check if brand belongs to the specified region
            if (RegionBrands.TryGetValue(normalizedOrigin, out var regionBrands))
                return regionBrands.Contains(car.Brand);

            return false;
        }

        private static string NormalizeOrigin(string origin)
        {
            if (RegionAliases.TryGetValue(origin, out var normalized))
                return normalized;
            return origin;
        }
    }
}
