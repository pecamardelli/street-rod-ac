using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Dealers;
using Street_Rod_AC.Services.Parts;

namespace Street_Rod_AC.Services.Market
{
    /// <summary>
    /// Implements used car market spawning and management
    /// </summary>
    public class UsedCarMarketService(IContentCatalogRepository catalogRepo, ICarProfileRepository profileRepo, ICarPartsService? partsService = null, IDealerCatalog? dealerCatalog = null) : IUsedCarMarketService
    {
        private readonly IContentCatalogRepository _catalogRepo = catalogRepo;
        private readonly ICarProfileRepository _profileRepo = profileRepo;
        private readonly ICarPartsService? _partsService = partsService;
        private readonly IDealerCatalog? _dealerCatalog = dealerCatalog;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("Market");
        private readonly Random _random = new();

        // Market configuration
        private const int MinMarketSize = 30;
        private const int MaxMarketSize = 50;
        private const float MinCondition = 0.6f;
        private const float MaxCondition = 0.95f;
        private const int MinMileage = 5000;
        private const int MaxMileage = 60000;
        private const float PriceVariationPercent = 0.2f; // ±20%
        private const double ModificationsPriceShare = 0.5; // money put into an engine never comes back in full

        /// <summary>How hard a dealer pulls condition toward its own standard. At 1 the shift is the full
        /// distance between that standard and the middle of the range</summary>
        private const float DealerConditionPull = 1.4f;

        public async Task<List<UsedCarListing>> SpawnListingsAsync(List<DealerLocation> dealers, DateTime currentDate)
        {
            var listings = CreateListings(dealers, currentDate);
            await AddEnginesAsync(listings);
            return listings;
        }

        /// <summary>The cars for sale, still without their engines</summary>
        private List<UsedCarListing> CreateListings(List<DealerLocation> dealers, DateTime currentDate)
        {
            _logger.Information("Spawning used car market listings for date {Date}", currentDate);

            var listings = new List<UsedCarListing>();
            var activeCars = _catalogRepo.GetCarsByStatus(ContentStatus.Active);

            _logger.Information("Found {CarCount} active cars to spawn from", activeCars.Count);

            // Where each car sits in the market's price range decides which lot it lands on
            var priceLadder = GetPriceLadder(activeCars);

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
                    var listing = CreateListing(car, profile, dealers, currentDate, priceLadder);
                    listings.Add(listing);
                }
            }

            _logger.Information("Spawned {ListingCount} total listings across {DealerCount} dealers",
                listings.Count, dealers.Count);

            return listings;
        }

        public async Task<List<UsedCarListing>> RefreshMarketAsync(List<UsedCarListing> currentListings, List<DealerLocation> dealers, DateTime currentDate)
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

            // Spawn new listings (simplified - spawn from all cars).
            // Engines only for the ones that make it into the market: putting one together is the costly part.
            if (toSpawn > 0)
            {
                var newListings = CreateListings(dealers, currentDate).Take(toSpawn).ToList();
                await AddEnginesAsync(newListings);
                kept.AddRange(newListings);
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
                new() { Id = "downtown_motors", Name = "Downtown Motors", Region = "Downtown" },
                new() { Id = "eastside_garage", Name = "Eastside Garage", Region = "Eastside" },
                new() { Id = "suburban_autos", Name = "Suburban Autos", Region = "Suburbs" },
                new() { Id = "riverside_cars", Name = "Riverside Cars", Region = "Riverside" },
                new() { Id = "industrial_motors", Name = "Industrial Motors", Region = "Industrial District" }
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

        private UsedCarListing CreateListing(CarDefinition carDef, CarProfile profile, List<DealerLocation> dealers, DateTime currentDate,
            List<decimal> priceLadder)
        {
            // The lot comes first: a cheap lot's cars are rougher, and that is what the price is worked out from
            var dealer = PickDealer(profile.BasePrice, dealers, priceLadder);
            var definition = _dealerCatalog?.Get(dealer.Id);

            var condition = GenerateCondition(definition);
            var mileage = GenerateMileage();
            var price = CalculatePrice(profile.BasePrice, condition);

            // Get random skin from available skins
            var skinId = "default";
            if (carDef.AvailableSkins != null && carDef.AvailableSkins.Count > 0)
            {
                skinId = carDef.AvailableSkins[_random.Next(carDef.AvailableSkins.Count)];
            }

            var listing = new UsedCarListing
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

            return listing;
        }

        /// <summary>
        /// A worked-on engine is tried out on the dyno a couple of dozen times: not on the caller's thread.
        /// The listings are new and nobody else's yet, so they can be filled in from there.
        /// </summary>
        private Task AddEnginesAsync(List<UsedCarListing> listings) => Task.Run(() =>
        {
            if (_partsService is not { IsAvailable: true }) return;

            foreach (var listing in listings)
            {
                if (_catalogRepo.GetCar(listing.CarDefinitionId) is { } carDef) AddEngine(_partsService, listing, carDef);
            }
        });

        /// <summary>
        /// The car is sold with the engine it has: mostly the factory one, now and then worked on.
        /// What was put into it shows in the price, though never in full.
        /// </summary>
        private void AddEngine(ICarPartsService parts, UsedCarListing listing, CarDefinition carDef)
        {
            try
            {
                var engine = parts.CreateUsedEngine(carDef, listing.Condition);
                if (engine == null) return;

                listing.Parts.Add(engine.Root);
                listing.EngineSummary = parts.Describe(engine.Root, engine.Report);
                listing.IsModified = engine.IsModified;

                // Random.Shared: this runs on a worker thread, the service's own Random belongs to the caller's
                if (engine.IsModified && parts.GetStockBuild(carDef) is { } stockBuild
                    && EngineFactory.CreateStock(parts.Catalog, stockBuild, listing.Condition, Random.Shared) is { } stock)
                {
                    var extra = PartPricing.WorthOfAssembly(parts.Catalog, engine.Root) - PartPricing.WorthOfAssembly(parts.Catalog, stock.Root);
                    if (extra > 0) listing.Price += Math.Round((decimal)(extra * ModificationsPriceShare) / 100) * 100;
                }
            }
            catch (Exception ex)
            {
                // A listing without parts still sells; the buyer gets the factory engine then
                _logger.Warning("Could not build the engine of a {CarId}: {Error}", carDef.Id, ex.Message);
            }
        }

        /// <summary>
        /// Every base price in the market, sorted. A car is placed by how many cars it is dearer than rather
        /// than by where it falls between the cheapest and the dearest: one half-million-dollar car in the
        /// install would otherwise push everything else down into the bottom tenth of the range and leave the
        /// smart showroom with an empty floor.
        /// </summary>
        private List<decimal> GetPriceLadder(List<CarDefinition> activeCars)
        {
            var prices = activeCars
                .Select(car => _profileRepo.GetProfile(car.Id))
                .Where(profile => profile != null && profile.BasePrice > 0)
                .Select(profile => profile!.BasePrice)
                .ToList();

            prices.Sort();
            return prices;
        }

        /// <summary>
        /// Which lot a car ends up on. Dealers claim a slice of the price range, so the smart showroom gets the
        /// expensive metal and the dirt lot gets the cheap. Where slices overlap, the roll decides.
        ///
        /// Without dealer definitions there is nothing to go on and it falls back to the old free-for-all.
        /// </summary>
        private DealerLocation PickDealer(decimal basePrice, List<DealerLocation> dealers, List<decimal> priceLadder)
        {
            if (dealers.Count == 0) throw new InvalidOperationException("No dealers to put a car with");

            if (_dealerCatalog == null || _dealerCatalog.All.Count == 0 || priceLadder.Count == 0)
            {
                return dealers[_random.Next(dealers.Count)];
            }

            // Where this car sits in the market, 0 cheapest to 1 dearest: the share of cars it is dearer than
            var below = priceLadder.Count(p => p < basePrice);
            var rank = priceLadder.Count == 1 ? 0.5f : (float)below / (priceLadder.Count - 1);
            rank = Math.Clamp(rank, 0f, 1f);

            var fits = dealers
                .Where(d => _dealerCatalog.Get(d.Id) is { } def && rank >= def.PriceBandLow && rank <= def.PriceBandHigh)
                .ToList();

            if (fits.Count > 0) return fits[_random.Next(fits.Count)];

            // Outside everybody's band: give it to whoever reaches closest
            var nearest = dealers
                .Select(d => new { Dealer = d, Definition = _dealerCatalog.Get(d.Id) })
                .Where(x => x.Definition != null)
                .OrderBy(x => Math.Min(Math.Abs(rank - x.Definition!.PriceBandLow), Math.Abs(rank - x.Definition!.PriceBandHigh)))
                .FirstOrDefault();

            return nearest?.Dealer ?? dealers[_random.Next(dealers.Count)];
        }

        /// <summary>
        /// How straight the car is. A dealer pulls the roll toward the sort of stock it keeps, but the roll
        /// still has the bigger say, so a gem turns up on the dirt lot now and then and a dog turns up in the
        /// smart showroom. That is what makes looking round worth the drive.
        /// </summary>
        private float GenerateCondition(DealerDefinition? dealer)
        {
            // Generate condition between min and max
            var range = MaxCondition - MinCondition;
            var roll = MinCondition + (float)(_random.NextDouble() * range);

            if (dealer == null) return roll;

            // One car in twelve is not what the lot usually keeps: the trade-in nobody looked at properly,
            // or the tired one that slipped into the smart showroom. This is what makes the drive worth it.
            if (_random.Next(12) == 0) return roll;

            // The roll leads; the dealer shifts it by how far its own standard sits from the middle of the
            // range. A weighted average instead would squeeze every lot into the same narrow band.
            var middle = (MinCondition + MaxCondition) / 2f;
            var shifted = roll + (dealer.ConditionCenter - middle) * DealerConditionPull;
            return Math.Clamp(shifted, 0.15f, 1f);
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
