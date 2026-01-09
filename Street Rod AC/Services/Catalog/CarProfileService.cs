using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Catalog;

namespace Street_Rod_AC.Services.Catalog
{
    /// <summary>
    /// Implements car profile generation with pricing and precedence algorithms
    /// </summary>
    public class CarProfileService : ICarProfileService
    {
        private readonly IContentCatalogRepository _catalogRepo;
        private readonly ICarProfileRepository _profileRepo;
        private readonly IAppLogger _logger;

        // Brand reputation multipliers for pricing
        private static readonly Dictionary<string, float> BrandMultipliers = new()
        {
            // Luxury/Exotic brands
            { "ferrari", 2.5f }, { "lamborghini", 2.5f }, { "mclaren", 2.3f },
            { "porsche", 2.0f }, { "maserati", 1.8f }, { "aston", 1.8f },

            // Performance brands
            { "bmw", 1.5f }, { "mercedes", 1.5f }, { "audi", 1.4f },
            { "corvette", 1.4f }, { "nissan", 1.2f },

            // Common brands (default)
            { "ford", 1.0f }, { "chevrolet", 1.0f }, { "toyota", 0.9f },
            { "mazda", 0.9f }, { "honda", 0.9f }
        };

        public CarProfileService(IContentCatalogRepository catalogRepo, ICarProfileRepository profileRepo)
        {
            _catalogRepo = catalogRepo;
            _profileRepo = profileRepo;
            _logger = AppLoggerFactory.CreateLogger(LogCategory.Catalog);
        }

        public CarProfile GenerateDefaultProfile(CarDefinition carDefinition)
        {
            var basePrice = CalculateBasePrice(carDefinition);
            var precedence = CalculatePrecedence(carDefinition);

            _logger.Debug("Generated profile for {CarId}: BasePrice=${Price}, Precedence={Precedence}",
                carDefinition.Id, basePrice, precedence);

            return new CarProfile
            {
                CarDefinitionId = carDefinition.Id,
                BasePrice = basePrice,
                DealerPrecedence = precedence,
                Source = ProfileDataSource.Generated,
                CreatedDate = DateTime.Now,
                LastUpdatedDate = DateTime.Now,
                IsStreetLegal = true // Default assumption
            };
        }

        public decimal CalculateBasePrice(CarDefinition carDefinition)
        {
            // Start with power/weight ratio (primary metric)
            var powerWeightBase = CalculatePowerWeightPrice(carDefinition);

            // Apply brand multiplier
            var brandMultiplier = GetBrandMultiplier(carDefinition.Brand);
            var withBrand = powerWeightBase * (decimal)brandMultiplier;

            // Apply year adjustment
            var withYear = ApplyYearAdjustment(withBrand, carDefinition.Year);

            // Apply source quality multiplier
            var withSource = ApplySourceMultiplier(withYear, carDefinition.Source);

            // Clamp to reasonable range
            var clamped = Math.Clamp(withSource, 5000m, 500000m);

            // Round to nearest 100
            return Math.Round(clamped / 100) * 100;
        }

        public float CalculatePrecedence(CarDefinition carDefinition)
        {
            float precedence = 0.5f; // Start neutral

            // Recent cars more common
            if (carDefinition.Year.HasValue)
            {
                var year = carDefinition.Year.Value;
                if (year >= 2010) precedence += 0.3f;
                else if (year >= 2000) precedence += 0.2f;
                else if (year >= 1990) precedence += 0.1f;
                else if (year >= 1970) precedence += 0.0f;
                else precedence -= 0.1f; // Classic/vintage rarer
            }

            // Kunos cars more common (higher quality)
            if (carDefinition.Source == ContentSource.Kunos || carDefinition.Source == ContentSource.DLC)
            {
                precedence += 0.2f;
            }

            // Exotic brands rarer
            var brand = carDefinition.Brand.ToLowerInvariant();
            if (brand.Contains("ferrari") || brand.Contains("lamborghini") || brand.Contains("mclaren"))
            {
                precedence -= 0.3f;
            }
            else if (brand.Contains("porsche") || brand.Contains("maserati"))
            {
                precedence -= 0.2f;
            }
            else if (brand.Contains("ford") || brand.Contains("toyota") || brand.Contains("honda"))
            {
                precedence += 0.2f; // Common brands
            }

            // Performance tier based on power/weight
            var powerWeight = CalculatePowerToWeightRatio(carDefinition);
            if (powerWeight > 300) // Exotic performance
            {
                precedence -= 0.2f;
            }
            else if (powerWeight < 100) // Economy cars
            {
                precedence += 0.2f;
            }

            // Clamp to valid range
            return Math.Clamp(precedence, 0.0f, 1.0f);
        }

        public async Task EnsureProfilesExistAsync()
        {
            _logger.Information("Ensuring car profiles exist for all car definitions");

            var allCars = _catalogRepo.GetAllCars();
            var activeCars = allCars.Where(c => c.Status == ContentStatus.Active).ToList();

            _logger.Information("Found {TotalCars} total cars, {ActiveCars} active",
                allCars.Count, activeCars.Count);

            int created = 0;
            int existing = 0;

            foreach (var car in activeCars)
            {
                if (!_profileRepo.ProfileExists(car.Id))
                {
                    var profile = GenerateDefaultProfile(car);
                    _profileRepo.UpsertProfile(profile);
                    created++;
                }
                else
                {
                    existing++;
                }
            }

            _logger.Information("Profile generation complete. Created: {Created}, Existing: {Existing}",
                created, existing);

            await Task.CompletedTask;
        }

        // Helper methods

        private decimal CalculatePowerWeightPrice(CarDefinition carDef)
        {
            var powerToWeight = CalculatePowerToWeightRatio(carDef);

            if (powerToWeight <= 0)
            {
                // No specs available, use conservative default
                return 15000m;
            }

            // Base formula: (BHP / Weight_kg) × 1000
            // This gives us a starting point
            var basePrice = (decimal)powerToWeight * 1000m;

            return basePrice;
        }

        private float CalculatePowerToWeightRatio(CarDefinition carDef)
        {
            if (carDef.Specs == null)
                return 0f;

            // Try to parse BHP
            if (!TryParseBhp(carDef.Specs.Bhp, out var bhp))
                return 0f;

            // Try to parse weight
            if (!TryParseWeight(carDef.Specs.Weight, out var weightKg))
                return 0f;

            if (weightKg <= 0)
                return 0f;

            return bhp / weightKg;
        }

        private bool TryParseBhp(string? bhpStr, out float bhp)
        {
            bhp = 0f;
            if (string.IsNullOrEmpty(bhpStr))
                return false;

            // Remove common suffixes and parse
            var cleaned = bhpStr.Replace("bhp", "", StringComparison.OrdinalIgnoreCase)
                                .Replace("hp", "", StringComparison.OrdinalIgnoreCase)
                                .Trim();

            return float.TryParse(cleaned, out bhp);
        }

        private bool TryParseWeight(string? weightStr, out float weightKg)
        {
            weightKg = 0f;
            if (string.IsNullOrEmpty(weightStr))
                return false;

            var cleaned = weightStr.Replace("kg", "", StringComparison.OrdinalIgnoreCase).Trim();
            return float.TryParse(cleaned, out weightKg);
        }

        private float GetBrandMultiplier(string brand)
        {
            var lowerBrand = brand.ToLowerInvariant();

            foreach (var kvp in BrandMultipliers)
            {
                if (lowerBrand.Contains(kvp.Key))
                {
                    return kvp.Value;
                }
            }

            return 1.0f; // Default multiplier
        }

        private decimal ApplyYearAdjustment(decimal basePrice, int? year)
        {
            if (!year.HasValue)
                return basePrice;

            var yearValue = year.Value;

            // Modern cars (2010+): slight premium
            if (yearValue >= 2010)
                return basePrice * 1.1m;

            // 2000s: no adjustment
            if (yearValue >= 2000)
                return basePrice;

            // 1990s: slight discount
            if (yearValue >= 1990)
                return basePrice * 0.9m;

            // 1980s: discount
            if (yearValue >= 1980)
                return basePrice * 0.7m;

            // 1970s: larger discount
            if (yearValue >= 1970)
                return basePrice * 0.6m;

            // 1960s and older: classic premium
            if (yearValue >= 1960)
                return basePrice * 1.2m;

            // Very old classics: high premium
            return basePrice * 1.5m;
        }

        private decimal ApplySourceMultiplier(decimal basePrice, ContentSource source)
        {
            return source switch
            {
                ContentSource.Kunos => basePrice * 1.2m,  // Official quality premium
                ContentSource.DLC => basePrice * 1.2m,     // Official DLC premium
                ContentSource.Mod => basePrice * 0.9m,     // Community mod discount
                _ => basePrice                             // Unknown
            };
        }
    }
}
