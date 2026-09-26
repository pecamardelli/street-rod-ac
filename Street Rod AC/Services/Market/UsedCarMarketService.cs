using Street_Rod_AC.Helpers;
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
        private const int DefaultStockLow = 8;
        private const int DefaultStockHigh = 14;

        /// <summary>The same model twice on one lot is a coincidence; three times is a car park</summary>
        private const int MaxSameModelPerDealer = 2;
        private const float MinCondition = 0.6f;
        private const float MaxCondition = 0.95f;
        private const int MinMileage = 5000;
        private const int MaxMileage = 60000;
        private const float PriceVariationPercent = 0.2f; // ±20%

        /// <summary>How hard a dealer pulls condition toward its own standard. At 1 the shift is the full
        /// distance between that standard and the middle of the range</summary>
        private const float DealerConditionPull = 1.4f;

        public async Task<List<UsedCarListing>> SpawnListingsAsync(List<DealerLocation> dealers, DateTime currentDate, double priceMultiplier)
        {
            var listings = CreateListings(dealers, currentDate);
            await AddEnginesAsync(listings);
            ApplyPriceMultiplier(listings, priceMultiplier);
            return listings;
        }

        /// <summary>
        /// The difficulty's say in what the sellers ask (<see cref="GameRules.CarPriceMultiplier"/>): on the whole price,
        /// the engine's modifications included, once the listing is complete
        /// </summary>
        private static void ApplyPriceMultiplier(List<UsedCarListing> listings, double priceMultiplier)
        {
            foreach (var listing in listings)
                listing.Price = CarValuation.RoundToHundred(GameRules.Scale(listing.Price, priceMultiplier));
        }

        /// <summary>
        /// The cars for sale, still without their engines.
        ///
        /// Each dealer is filled to its own target rather than the market being spawned whole and then cut
        /// down. Cutting a list built in catalog order kept whichever cars happened to come first and starved
        /// every dealer whose kind of car came later - a lot with nothing on it, while another held a hundred.
        /// </summary>
        private List<UsedCarListing> CreateListings(List<DealerLocation> dealers, DateTime currentDate)
        {
            _logger.Information("Spawning used car market listings for date {Date}", currentDate);

            var activeCars = _catalogRepo.GetCarsByStatus(ContentStatus.Active);
            _logger.Information("Found {CarCount} active cars to spawn from", activeCars.Count);

            var pool = BuildPool(activeCars);
            if (pool.Count == 0)
            {
                _logger.Warning("No car has a profile to price it; the market stays empty");
                return [];
            }

            var listings = new List<UsedCarListing>();
            foreach (var dealer in dealers)
            {
                var wanted = StockWantedBy(dealer);
                listings.AddRange(FillDealer(dealer, wanted, pool, currentDate));
            }

            _logger.Information("Spawned {ListingCount} listings across {DealerCount} dealers",
                listings.Count, dealers.Count);

            return listings;
        }

        /// <summary>A car that may turn up for sale, and where it sits in the market</summary>
        private readonly record struct PoolEntry(CarDefinition Car, CarProfile Profile, float Rank);

        /// <summary>
        /// Every sellable car with its place in the market worked out once: the share of cars it is dearer
        /// than. A share rather than a position between the cheapest and the dearest, because one
        /// half-million-dollar car in the install would otherwise push everything else into the bottom tenth
        /// and leave the smart showroom with an empty floor.
        /// </summary>
        private List<PoolEntry> BuildPool(List<CarDefinition> activeCars)
        {
            // The catalog outlives the install: a car deleted from content/cars stays on its books, and one that
            // is not there cannot be sold. It would take a place on a lot, show a card, and stand as an invisible
            // gap - which is what emptied the dearest lots, since the cars that were cleared out were the
            // expensive ones.
            var installed = InstalledCars.Only(activeCars, out var uninstalled);
            if (uninstalled > 0)
            {
                _logger.Warning("{Count} cars in the catalog are not installed and cannot be sold", uninstalled);
            }

            // One visit to the catalog for every profile, not one per car: this runs every game day
            var profiles = _profileRepo.GetAllProfiles().ToDictionary(p => p.CarDefinitionId, StringComparer.OrdinalIgnoreCase);

            var priced = new List<(CarDefinition Car, CarProfile Profile)>();
            foreach (var car in installed)
            {
                if (profiles.TryGetValue(car.Id, out var profile) && profile.BasePrice > 0) priced.Add((car, profile));
            }

            if (priced.Count == 0) return [];

            var ladder = priced.Select(p => p.Profile.BasePrice).OrderBy(p => p).ToList();
            var pool = new List<PoolEntry>(priced.Count);

            foreach (var (car, profile) in priced)
            {
                var below = CountBelow(ladder, profile.BasePrice);
                var rank = ladder.Count == 1 ? 0.5f : (float)below / (ladder.Count - 1);
                pool.Add(new PoolEntry(car, profile, Math.Clamp(rank, 0f, 1f)));
            }

            return pool;
        }

        /// <summary>How many prices of the sorted ladder are below <paramref name="price"/>: the first index not below it</summary>
        private static int CountBelow(List<decimal> ladder, decimal price)
        {
            int low = 0, high = ladder.Count;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (ladder[middle] < price) low = middle + 1;
                else high = middle;
            }

            return low;
        }

        /// <summary>How many cars this dealer means to have out, from its own definition</summary>
        private int StockWantedBy(DealerLocation dealer)
        {
            var definition = _dealerCatalog?.Get(dealer.Id);
            if (definition == null) return _random.Next(DefaultStockLow, DefaultStockHigh + 1);

            var low = Math.Max(1, definition.StockLow);
            var high = Math.Max(low, definition.StockHigh);
            return _random.Next(low, high + 1);
        }

        /// <summary>
        /// Stocks one lot. Cars are drawn from the slice of the market the dealer deals in, weighted by how
        /// common they are, and no model turns up more than twice on the same lot.
        /// </summary>
        private List<UsedCarListing> FillDealer(DealerLocation dealer, int wanted, List<PoolEntry> pool, DateTime currentDate)
        {
            var definition = _dealerCatalog?.Get(dealer.Id);
            var candidates = definition == null
                ? pool
                : pool.Where(e => e.Rank >= definition.PriceBandLow && e.Rank <= definition.PriceBandHigh).ToList();

            if (candidates.Count == 0 && definition != null)
            {
                // Nothing installed sits in this dealer's slice: let it take whatever comes nearest instead of
                // standing empty
                var middle = (definition.PriceBandLow + definition.PriceBandHigh) / 2f;
                candidates = pool.OrderBy(e => Math.Abs(e.Rank - middle)).Take(Math.Max(4, wanted)).ToList();
                _logger.Warning("{Dealer} deals in {Low:0.00}-{High:0.00} of the market and nothing installed fits; " +
                    "taking the nearest {Count}", dealer.Name, definition.PriceBandLow, definition.PriceBandHigh, candidates.Count);
            }

            if (candidates.Count == 0) return [];

            var listings = new List<UsedCarListing>(wanted);
            var used = new Dictionary<string, int>();

            // A car's precedence is how likely it is to turn up at all, so it is the weight to draw by
            var weights = candidates.Select(e => Math.Max(0.02f, e.Profile.DealerPrecedence)).ToList();
            var total = weights.Sum();

            for (var attempt = 0; attempt < wanted * 8 && listings.Count < wanted; attempt++)
            {
                var roll = (float)(_random.NextDouble() * total);
                var index = 0;
                while (index < weights.Count - 1 && roll > weights[index])
                {
                    roll -= weights[index];
                    index++;
                }

                var entry = candidates[index];
                used.TryGetValue(entry.Car.Id, out var already);
                if (already >= MaxSameModelPerDealer) continue;
                used[entry.Car.Id] = already + 1;

                listings.Add(CreateListing(entry.Car, entry.Profile, dealer, currentDate));
            }

            return listings;
        }

        public async Task<List<UsedCarListing>> RefreshMarketAsync(List<UsedCarListing> currentListings, List<DealerLocation> dealers, DateTime currentDate, double priceMultiplier)
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

            // Every lot is topped back up to its own target. Spawning a whole market and cutting it down to
            // size used to keep whichever cars came first in the catalog, which left some lots bare.
            // Engines only for the ones that make it into the market: putting one together is the costly part.
            var pool = BuildPool(_catalogRepo.GetCarsByStatus(ContentStatus.Active));
            if (pool.Count > 0)
            {
                var fresh = new List<UsedCarListing>();

                foreach (var dealer in dealers)
                {
                    var onTheLot = kept.Count(l => !l.IsSold && l.DealerLocation == dealer.Id);
                    var shortBy = StockWantedBy(dealer) - onTheLot;
                    if (shortBy <= 0) continue;

                    fresh.AddRange(FillDealer(dealer, shortBy, pool, currentDate));
                }

                if (fresh.Count > 0)
                {
                    await AddEnginesAsync(fresh);
                    ApplyPriceMultiplier(fresh, priceMultiplier);
                    kept.AddRange(fresh);
                }

                _logger.Information("Topped {Count} cars up across {Dealers} lots", fresh.Count, dealers.Count);
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
            // The dealers of the dealer file, when there is one: the same the map shows
            try
            {
                if (_dealerCatalog?.ToLocations() is { Count: > 0 } known) return known;
            }
            catch (Exception ex)
            {
                _logger.Warning("Could not read the dealers: going with the built-in ones ({Error})", ex.Message);
            }

            return new List<DealerLocation>
            {
                new() { Id = "downtown_motors", Name = "Downtown Motors", Region = "Downtown" },
                new() { Id = "eastside_garage", Name = "Eastside Garage", Region = "Eastside" },
                new() { Id = "suburban_autos", Name = "Suburban Autos", Region = "Suburbs" },
                new() { Id = "riverside_cars", Name = "Riverside Cars", Region = "Riverside" },
                new() { Id = "industrial_motors", Name = "Industrial Motors", Region = "Industrial District" }
            };
        }

        /// <summary>
        /// The least a car goes back on a lot for. A car the catalog cannot price and nobody paid for (no profile, a
        /// purchase price of 0) is valued at nothing, and a listing at $0 is a free car.
        /// </summary>
        private const decimal MinListPrice = 500m;

        public UsedCarListing ListCar(Car car, decimal price, string location, DateTime listedDate, bool describeEngine = true)
        {
            // What a race did to the car is not always sane: a NaN odometer casts to int.MinValue, and an opponent's
            // health values carry a little noise above 1. The buyer gets the car at these numbers, so they are made sane here.
            var condition = CarValuation.ConditionOf(car);
            var listing = new UsedCarListing
            {
                Id = Guid.NewGuid().ToString(),
                CarDefinitionId = car.DefinitionId,
                Price = CarValuation.RoundToHundred(Math.Max(price, MinListPrice)),
                Mileage = double.IsFinite(car.OdometerKM) ? (int)Math.Clamp(car.OdometerKM, 0, int.MaxValue) : 0,
                Condition = (float)Unit.Clamp01(condition, ifNotFinite: 0),
                SkinId = string.IsNullOrEmpty(car.SkinId) ? "default" : car.SkinId,
                ListedDate = listedDate,
                DealerLocation = location,
                IsSold = false,

                // The car goes with everything on it; the buyer gets exactly this car
                Parts = car.Parts,
                HasRunningGearAssigned = car.HasRunningGearAssigned,
                BodyDamageKmh = CarCondition.BodyTotal(car) > 0 ? CarCondition.Body(car) : null,

                // Its past goes with it: who had it, how it raced
                History = car.History?.Copy() ?? new CarHistory(),
                CarInstanceId = car.InstanceId
            };

            if (describeEngine && listing.Parts.Count > 0 && DescribeEngine(car) is { } engine)
            {
                listing.EngineSummary = engine.Summary;
                listing.PowerHp = engine.PowerHp;
                listing.IsModified = engine.IsModified;
            }

            return listing;
        }

        public EngineDescription? DescribeEngine(Car car)
        {
            if (car.Engine is not { } engine || _partsService is not { IsAvailable: true } parts) return null;

            try
            {
                var report = parts.Evaluate(car);
                var modified = _catalogRepo.GetCar(car.DefinitionId) is { } carDef && parts.GetStockBuild(carDef) is { } stock
                               && CarValuation.IsModified(engine, stock);
                return new EngineDescription(parts.Describe(engine, report), PowerOf(report), modified);
            }
            catch (Exception ex)
            {
                // The car still sells with its parts; the seller just has less to say about the engine
                _logger.Warning("Could not describe the engine of a {CarId}: {Error}", car.DefinitionId, ex.Message);
                return null;
            }
        }

        /// <summary>The dyno's horsepower; 0 for an engine that does not run</summary>
        public static double PowerOf(Street_Rod_AC.Parts.Logic.EngineReport? report) => report is { Runs: true } ? report.Dyno!.MaxPowerHp : 0;

        public decimal ValueOf(Car car) =>
            CarValuation.WorthOf(car, _profileRepo.GetProfile, _catalogRepo.GetCar, _partsService);

        public Func<Car, decimal> Valuer()
        {
            // Each model's profile, catalog entry and factory build read once, not once per car and per ask. Only
            // those: the cars themselves change as the review goes (a repair, a new engine) and are weighed each time.
            var profiles = new Dictionary<string, CarProfile?>(StringComparer.OrdinalIgnoreCase);
            var definitions = new Dictionary<string, CarDefinition?>(StringComparer.OrdinalIgnoreCase);
            var stockBuilds = new Dictionary<string, RatedBuild?>(StringComparer.OrdinalIgnoreCase);
            var parts = _partsService;

            return car => CarValuation.WorthOf(car,
                id => Remembered(profiles, id, _profileRepo.GetProfile),
                id => Remembered(definitions, id, _catalogRepo.GetCar),
                parts,
                definition => Remembered(stockBuilds, definition.Id, _ => parts!.GetStockBuild(definition)));
        }

        private static T? Remembered<T>(Dictionary<string, T?> seen, string key, Func<string, T?> read)
        {
            if (seen.TryGetValue(key, out var known)) return known;
            var value = read(key);
            seen[key] = value;
            return value;
        }

        private const string TradeInFallbackId = "industrial_motors";

        public string TradeInLocation(IReadOnlyList<DealerLocation>? dealers)
        {
            var known = dealers is { Count: > 0 } ? dealers : GetDefaultDealers();

            // The lot that keeps the roughest stock takes the cars nobody asked about: the trade-ins
            var roughest = known
                .Select(d => (Location: d, Definition: _dealerCatalog?.Get(d.Id)))
                .Where(d => d.Definition != null)
                .OrderBy(d => d.Definition!.ConditionCenter)
                .Select(d => d.Location)
                .FirstOrDefault();

            // No dealer the catalog knows: the one of the default five that always took the trade-ins, when this
            // game has it, rather than whichever dealer happens to be last
            return (roughest
                ?? known.FirstOrDefault(d => string.Equals(d.Id, TradeInFallbackId, StringComparison.OrdinalIgnoreCase))
                ?? known[^1]).Id;
        }

        private UsedCarListing CreateListing(CarDefinition carDef, CarProfile profile, DealerLocation dealer, DateTime currentDate)
        {
            // A cheap lot's cars are rougher, and that is what the price is worked out from
            var definition = _dealerCatalog?.Get(dealer.Id);

            var condition = GenerateCondition(definition);
            var mileage = GenerateMileage();
            var history = new CarHistory { EarlierOwners = GenerateEarlierOwners(mileage) };
            var price = CalculatePrice(profile.BasePrice, condition, CarValuation.HistoryFactor(history, mileage));

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
                IsSold = false,
                History = history
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
                listing.PowerHp = PowerOf(engine.Report);

                // Worked on, and what that adds, by the same rules the car is valued by once it is somebody's
                if (parts.GetStockBuild(carDef) is { } stockBuild)
                {
                    listing.IsModified = CarValuation.IsModified(engine.Root, stockBuild);
                    if (listing.IsModified)
                    {
                        listing.Price += CarValuation.RoundToHundred(
                            CarValuation.EngineModifications(parts.Catalog, engine.Root, stockBuild, listing.Condition));
                    }
                }
            }
            catch (Exception ex)
            {
                // A listing without parts still sells; the buyer gets the factory engine then
                _logger.Warning("Could not build the engine of a {CarId}: {Error}", carDef.Id, ex.Message);
            }
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

        /// <summary>
        /// Who had the car before the lot did: one owner for a car that has not gone far, one more for every
        /// <see cref="KmPerEarlierOwner"/> or so
        /// </summary>
        private int GenerateEarlierOwners(int mileage) =>
            1 + (int)Math.Floor(mileage / KmPerEarlierOwner * _random.NextDouble() * 2);

        private const double KmPerEarlierOwner = 40_000;

        private decimal CalculatePrice(decimal basePrice, float condition, decimal history)
        {
            // What the car is worth (the one condition curve every price in the game uses, and a little for its
            // past), and then what this dealer makes of it on the day: a random variation of ±20%
            var price = basePrice * CarValuation.ConditionFactor(condition) * history;
            var variation = 1.0m + ((decimal)_random.NextDouble() * (decimal)PriceVariationPercent * 2) - (decimal)PriceVariationPercent;
            price *= variation;

            return CarValuation.RoundToHundred(price);
        }
    }
}
