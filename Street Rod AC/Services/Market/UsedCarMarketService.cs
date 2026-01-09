using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Services.Catalog;

namespace Street_Rod_AC.Services.Market
{
    /// <summary>
    /// Implements used car market spawning and management
    /// </summary>
    public class UsedCarMarketService : IUsedCarMarketService
    {
        private readonly IContentCatalogRepository _catalogRepo;
        private readonly ICarProfileRepository _profileRepo;
        private readonly IAppLogger _logger;
        private readonly Random _random = new Random();

        // Market configuration
        private const int MinMarketSize = 30;
        private const int MaxMarketSize = 50;
        private const float MinCondition = 0.3f;
        private const float MaxCondition = 0.95f;
        private const int MinMileage = 5000;
        private const int MaxMileage = 200000;
        private const float PriceVariationPercent = 0.2f; // ±20%

        public UsedCarMarketService(IContentCatalogRepository catalogRepo, ICarProfileRepository profileRepo)
        {
            _catalogRepo = catalogRepo;
            _profileRepo = profileRepo;
            _logger = AppLoggerFactory.CreateLogger("Market");
        }

        public List<UsedCarListing> SpawnListings(List<DealerLocation> dealers, DateTime currentDate)
        {
            _logger.Information("Spawning used car market listings for date {Date}", currentDate);

            var listings = new List<UsedCarListing>();
            var activeCars = _catalogRepo.GetCarsByStatus(ContentStatus.Active);

            _logger.Information("Found {CarCount} active cars to spawn from", activeCars.Count);

            // Spawn listings based on precedence
            foreach (var car in activeCars)
            {
                var profile = _profileRepo.GetProfile(car.Id);
                if (profile == null)
                {
                    _logger.Warning("No profile found for car {CarId}, skipping", car.Id);
                    continue;
                }

                // Determine how many instances to spawn based on precedence
                var instanceCount = DetermineInstanceCount(profile.DealerPrecedence);

                for (int i = 0; i < instanceCount; i++)
                {
                    var listing = CreateListing(car, profile, dealers, currentDate);
                    listings.Add(listing);
                }
            }

            _logger.Information("Spawned {ListingCount} total listings across {DealerCount} dealers",
                listings.Count, dealers.Count);

            return listings;
        }

        public List<UsedCarListing> RefreshMarket(List<UsedCarListing> currentListings, List<DealerLocation> dealers, DateTime currentDate)
        {
            _logger.Information("Refreshing used car market for date {Date}", currentDate);

            // Remove old sold listings (older than 7 days)
            var oldSoldCutoff = currentDate.AddDays(-7);
            var filtered = currentListings.Where(l =>
                !l.IsSold || (l.SoldDate.HasValue && l.SoldDate.Value > oldSoldCutoff)).ToList();

            var removedSold = currentListings.Count - filtered.Count;
            if (removedSold > 0)
            {
                _logger.Information("Removed {Count} old sold listings", removedSold);
            }

            // Despawn old unsold listings (older than 14 days)
            var unsoldCutoff = currentDate.AddDays(-14);
            var kept = filtered.Where(l =>
                l.IsSold || l.ListedDate > unsoldCutoff).ToList();

            var removedUnsold = filtered.Count - kept.Count;
            if (removedUnsold > 0)
            {
                _logger.Information("Despawned {Count} old unsold listings", removedUnsold);
            }

            // Calculate how many new listings to spawn
            var availableCount = kept.Count(l => !l.IsSold);
            var targetSize = _random.Next(MinMarketSize, MaxMarketSize + 1);
            var toSpawn = Math.Max(0, targetSize - availableCount);

            _logger.Information("Current available: {Available}, Target: {Target}, Will spawn: {ToSpawn}",
                availableCount, targetSize, toSpawn);

            // Spawn new listings (simplified - spawn from all cars)
            if (toSpawn > 0)
            {
                var newListings = SpawnListings(dealers, currentDate);
                kept.AddRange(newListings.Take(toSpawn));
            }

            _logger.Information("Market refresh complete. Total listings: {Total}, Available: {Available}",
                kept.Count, kept.Count(l => !l.IsSold));

            return kept;
        }

        public List<UsedCarListing> GetAvailableListings(List<UsedCarListing> allListings)
        {
            return allListings.Where(l => !l.IsSold).ToList();
        }

        public List<UsedCarListing> GetListingsByDealer(List<UsedCarListing> allListings, string dealerLocationId)
        {
            return allListings.Where(l => !l.IsSold && l.DealerLocation == dealerLocationId).ToList();
        }

        public List<DealerLocation> GetDefaultDealers()
        {
            return new List<DealerLocation>
            {
                new DealerLocation { Id = "downtown_motors", Name = "Downtown Motors", Region = "Downtown" },
                new DealerLocation { Id = "eastside_garage", Name = "Eastside Garage", Region = "Eastside" },
                new DealerLocation { Id = "suburban_autos", Name = "Suburban Autos", Region = "Suburbs" },
                new DealerLocation { Id = "riverside_cars", Name = "Riverside Cars", Region = "Riverside" },
                new DealerLocation { Id = "industrial_motors", Name = "Industrial Motors", Region = "Industrial District" }
            };
        }

        // Helper methods

        private int DetermineInstanceCount(float precedence)
        {
            // Roll random to determine if we spawn at all
            var roll = (float)_random.NextDouble();

            if (roll >= precedence)
            {
                return 0; // Don't spawn
            }

            // Spawn count based on precedence
            if (precedence >= 0.7f)
            {
                return _random.Next(1, 4); // 1-3 instances
            }
            else if (precedence >= 0.4f)
            {
                return _random.Next(0, 3); // 0-2 instances
            }
            else
            {
                return _random.Next(0, 2); // 0-1 instance
            }
        }

        private UsedCarListing CreateListing(CarDefinition carDef, CarProfile profile, List<DealerLocation> dealers, DateTime currentDate)
        {
            var condition = GenerateCondition();
            var mileage = GenerateMileage();
            var price = CalculatePrice(profile.BasePrice, condition);
            var skinId = "default"; // TODO: Get random skin from car skins
            var dealer = dealers[_random.Next(dealers.Count)];

            return new UsedCarListing
            {
                Id = Guid.NewGuid().ToString(),
                CarDefinitionId = carDef.Id,
                Price = price,
                Mileage = mileage,
                Condition = condition,
                SkinId = skinId,
                ListedDate = currentDate,
                DealerLocation = dealer.Id,
                IsSold = false
            };
        }

        private float GenerateCondition()
        {
            // Generate condition between min and max
            var range = MaxCondition - MinCondition;
            return MinCondition + (float)(_random.NextDouble() * range);
        }

        private int GenerateMileage()
        {
            return _random.Next(MinMileage, MaxMileage + 1);
        }

        private decimal CalculatePrice(decimal basePrice, float condition)
        {
            // Start with base price
            var price = basePrice;

            // Apply condition multiplier (0.3 condition = 50% price, 1.0 condition = 110% price)
            var conditionMultiplier = 0.5m + ((decimal)condition * 0.6m);
            price *= conditionMultiplier;

            // Add random variation ±20%
            var variation = 1.0m + ((decimal)_random.NextDouble() * (decimal)PriceVariationPercent * 2) - (decimal)PriceVariationPercent;
            price *= variation;

            // Round to nearest 100
            return Math.Round(price / 100) * 100;
        }
    }
}
