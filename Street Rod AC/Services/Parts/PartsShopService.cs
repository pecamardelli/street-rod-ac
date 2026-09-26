using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts;
using Street_Rod_AC.Parts.Cars;

namespace Street_Rod_AC.Services.Parts
{
    /// <summary>
    /// Buying and selling parts: new ones by mail order at the part's own price, used ones from the ads, and
    /// the player's own to whoever takes them. Everything bought goes to the player's shelf.
    /// </summary>
    public class PartsShopService : IPartsShopService
    {
        private const string EnginePacks = "engines/";

        private const int MinAds = 25;
        private const int MaxAds = 40;
        private const int AdLifetimeDays = 10;
        private const double MinWear = 0.35;
        private const double MaxWear = 0.92;

        // Now and then somebody sells a whole engine instead of its parts
        private const double CompleteEngineChance = 0.08;

        private static readonly string[] Sellers =
        {
            "Al's Speed Shop", "Frank", "Eddie's Salvage", "Bobby", "Route 9 Wreckers", "Hank", "Mel's Machine Shop",
            "Ray", "Lou", "Valley Auto Parts", "Skip", "Dutch", "Big Jim", "Walt", "Southside Garage"
        };

        private readonly ICarPartsService _parts;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger(LogCategory.Parts);
        private readonly Lazy<IReadOnlyList<PartDefinition>> _assortment;

        public PartsShopService(ICarPartsService parts)
        {
            _parts = parts;
            _assortment = new Lazy<IReadOnlyList<PartDefinition>>(FindAssortment);
        }

        public bool IsAvailable => _parts.IsAvailable;

        public IReadOnlyList<PartDefinition> Assortment => _assortment.Value;

        public decimal NewPrice(PartDefinition part, double priceMultiplier) =>
            PartPricing.Round(PartPricing.NewPrice(part) * GameRules.Sane(priceMultiplier));

        public decimal TradeInPrice(PartInstance part) => PartPricing.TradeIn(_parts.Catalog, part);

        public async Task RefreshAdsAsync(Models.GameState.GameState gameState, DateTime currentDate)
        {
            var ads = gameState.NewspaperAds.Parts;
            var target = Random.Shared.Next(MinAds, MaxAds + 1);
            var wanted = target - ads.Count(ad => !IsExpired(ad, currentDate));

            // Loose from the game state: the list of ads belongs to the caller's thread, which may be saving it right now
            var priceMultiplier = gameState.Rules.PartPriceMultiplier;
            var fresh = await Task.Run(() => IsAvailable ? CreateAds(wanted, currentDate, priceMultiplier) : null);
            if (fresh == null) return;

            // A rival whose part nobody wanted lets a shop have it
            foreach (var ad in ads.Where(ad => ad.SellerRival != null && IsExpired(ad, currentDate)))
            {
                RivalPartAds.Pay(gameState, ad, TradeInPrice(ad.Part));
            }

            var expired = ads.RemoveAll(ad => IsExpired(ad, currentDate) || _parts.Catalog.Get(ad.Part.DefinitionId) == null);
            foreach (var ad in ads) ad.DaysActive = Math.Max(0, (int)(currentDate - ad.PostedDate).TotalDays);

            // No more than asked for, whatever else filled the paper while the ads were made
            var added = fresh.Take(Math.Max(0, target - ads.Count)).ToList();
            ads.AddRange(added);

            _logger.Information("Used parts ads: {Expired} gone, {Added} new, {Total} in the paper", expired, added.Count, ads.Count);
        }

        internal static bool IsExpired(PartAd ad, DateTime currentDate) => (currentDate - ad.PostedDate).TotalDays > AdLifetimeDays;

        private List<PartAd> CreateAds(int count, DateTime currentDate, double priceMultiplier)
        {
            var ads = new List<PartAd>();
            while (ads.Count < count && CreateAd(currentDate, priceMultiplier) is { } ad) ads.Add(ad);
            return ads;
        }

        public HashSet<string> FindFittingParts(Car? car)
        {
            var fitting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (car == null || !IsAvailable) return fitting;

            var catalog = _parts.Catalog;

            // With the engine bay empty any engine goes in
            if (car.Engine == null)
            {
                foreach (var part in Assortment.Where(PartKinds.IsBlock)) fitting.Add(part.Id);
            }

            // The running gear goes on the car itself; a tyre on a rim it fits
            var rims = RunningGear.Rims(car.Parts).Select(p => catalog.Get(p.DefinitionId)).Where(r => r != null).ToList();
            foreach (var part in Assortment)
            {
                var group = PartKinds.GroupOf(part);
                if (group == PartKinds.Tyres ? rims.Any(r => RunningGear.TyreFitsRim(part, r!)) : RunningGear.IsRunningGear(group)) fitting.Add(part.Id);
            }

            // Taken slots count as well: what is on them can come off
            foreach (var mounted in car.Parts.SelectMany(p => p.SelfAndDescendants()))
            {
                if (catalog.Get(mounted.DefinitionId) is not { } definition) continue;
                if (RunningGear.IsRunningGear(PartKinds.GroupOf(definition))) continue;

                foreach (var slot in definition.Slots)
                {
                    if (slot.Id == mounted.OwnSlot) continue;
                    foreach (var (part, _) in catalog.FindMountable(definition, slot)) fitting.Add(part.Id);
                }
            }

            return fitting;
        }

        public bool BuyNew(Models.GameState.GameState gameState, PartDefinition part)
        {
            var price = NewPrice(part, gameState.Rules.PartPriceMultiplier);
            if (gameState.Player.Money < price) return false;

            gameState.Player.Money -= price;
            gameState.Player.Parts.Add(new PartInstance(part.Id));
            _logger.Information("Bought new {Part} for ${Price}", part.Id, price);
            return true;
        }

        public bool BuyUsed(Models.GameState.GameState gameState, PartAd ad)
        {
            if (gameState.Player.Money < ad.AskingPrice || !gameState.NewspaperAds.Parts.Remove(ad)) return false;

            gameState.Player.Money -= ad.AskingPrice;
            gameState.Player.Parts.Add(ad.Part);
            RivalPartAds.Pay(gameState, ad, ad.AskingPrice);
            _logger.Information("Bought used {Part} from {Seller} for ${Price}", ad.Part.DefinitionId, ad.SellerName, ad.AskingPrice);
            return true;
        }

        public bool Sell(Models.GameState.GameState gameState, PartInstance part)
        {
            if (!gameState.Player.Parts.Remove(part)) return false;

            var price = TradeInPrice(part);
            gameState.Player.Money += price;
            _logger.Information("Sold {Part} for ${Price}", part.DefinitionId, price);
            return true;
        }

        private PartAd? CreateAd(DateTime currentDate, double priceMultiplier)
        {
            var random = Random.Shared;
            var catalog = _parts.Catalog;
            var wear = MinWear + random.NextDouble() * (MaxWear - MinWear);

            PartInstance? part = null;
            if (random.NextDouble() < CompleteEngineChance && _parts.Builds.Runnable.Count > 0)
            {
                var build = _parts.Builds.Runnable[random.Next(_parts.Builds.Runnable.Count)];
                part = EngineFactory.CreateStock(catalog, build, wear, random)?.Root;
                if (part != null) part.ParentSlot = 0;
            }

            if (part == null)
            {
                if (Assortment.Count == 0) return null;
                part = new PartInstance(Assortment[random.Next(Assortment.Count)].Id) { Wear = wear };
            }

            // Private sellers ask around what a shop would, some more, some less, and more in a harder game
            var asking = PartPricing.WorthOfAssembly(catalog, part) * PartPricing.UsedShopFactor * (0.8 + random.NextDouble() * 0.4)
                         * GameRules.Sane(priceMultiplier);
            return new PartAd(part, PartPricing.Round(asking), Sellers[random.Next(Sellers.Length)])
            {
                PostedDate = currentDate.AddDays(-random.Next(0, 4))
            };
        }

        /// <summary>
        /// What is for sale: the engine packs, whatever else the engine builds use (batteries, mufflers), and
        /// the running gear: tyres, rims, brakes, springs and shocks. The base game's scriptless entries (the
        /// roots other parts declare their fit against) are nothing to buy, and neither is what has no say in
        /// how the car drives (sway bars and suspension arms are inert in the source game).
        /// </summary>
        private IReadOnlyList<PartDefinition> FindAssortment()
        {
            var catalog = _parts.Catalog;
            var usedByBuilds = catalog.EngineBuilds
                .SelectMany(b => b.Parts)
                .Where(p => p.Part != null)
                .Select(p => p.Part!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return catalog.Parts.Values
                .Where(p => usedByBuilds.Contains(p.Id) ||
                            (p.Id.StartsWith(EnginePacks, StringComparison.OrdinalIgnoreCase) && p.IsScripted) ||
                            (p.IsScripted && RunningGear.IsRunningGear(PartKinds.GroupOf(p))))
                .OrderBy(p => p.DisplayName ?? p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
