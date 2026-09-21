using Street_Rod_AC.Configuration;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Parts.Logic;
using Street_Rod_AC.Services.Catalog;

namespace Street_Rod_AC.Services.Parts
{
    /// <summary>
    /// Ties the cars of the game to the parts catalog: which engine a car leaves the factory with, the parts a
    /// car instance carries, and what they come to on the dyno
    /// </summary>
    public class CarPartsService : ICarPartsService
    {
        // A car nobody has touched is still not a new car
        private const double UsedCarTuneChance = 0.35;
        private const double MinTuneLevel = 0.3;

        private readonly IContentCatalogRepository _catalogRepo;
        private readonly ICarProfileRepository _profileRepo;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger(LogCategory.Parts);
        private readonly Lazy<PartsCatalog> _catalog;
        private readonly Lazy<EngineBuildIndex> _builds;
        private readonly object _assignLock = new();

        public CarPartsService(IContentCatalogRepository catalogRepo, ICarProfileRepository profileRepo)
        {
            _catalogRepo = catalogRepo;
            _profileRepo = profileRepo;
            PartPricing.Scale = AppSettings.Instance.PartsPriceScale;
            _catalog = new Lazy<PartsCatalog>(LoadCatalog);
            _builds = new Lazy<EngineBuildIndex>(CreateIndex);
        }

        public PartsCatalog Catalog => _catalog.Value;

        public EngineBuildIndex Builds => _builds.Value;

        public bool IsAvailable => Catalog.Parts.Count > 0 && Builds.Runnable.Count > 0;

        public Task WarmUpAsync() => Task.Run(() =>
        {
            try
            {
                if (!IsAvailable)
                {
                    _logger.Warning("No converted parts under {Path}: cars go without part trees", AppSettings.Instance.PartsPath);
                    return;
                }

                SuggestStockEngines();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Parts warm-up failed");
            }
        });

        public RatedBuild? GetStockBuild(CarDefinition car)
        {
            if (!IsAvailable) return null;

            var profile = _profileRepo.GetProfile(car.Id);
            return Builds.Get(profile?.StockEngineBuildId) ?? Suggest(car);
        }

        public bool EnsureParts(Car car)
        {
            if (car.HasPartsAssigned || !IsAvailable) return false;

            return Assign(car, CreateFactoryEngine(car.DefinitionId, car.EngineHealth));
        }

        public async Task<bool> EnsurePartsAsync(Car car)
        {
            if (car.HasPartsAssigned) return false;

            // The engine is put together away from the caller's thread; the car is only touched back on it
            var definitionId = car.DefinitionId;
            var condition = car.EngineHealth;
            var engine = await Task.Run(() => IsAvailable ? CreateFactoryEngine(definitionId, condition) : null);
            return Assign(car, engine);
        }

        public BuiltEngine? CreateUsedEngine(CarDefinition car, double condition)
        {
            var build = GetStockBuild(car);
            if (build == null) return null;

            var tuneLevel = Random.Shared.NextDouble() < UsedCarTuneChance ? MinTuneLevel + (1 - MinTuneLevel) * Random.Shared.NextDouble() : 0;
            return tuneLevel > 0
                ? EngineFactory.CreateTuned(Catalog, Builds, build, condition, tuneLevel, Random.Shared)
                : EngineFactory.CreateStock(Catalog, build, condition, Random.Shared);
        }

        public string? Describe(PartInstance engine, EngineReport? report)
        {
            var block = Catalog.Get(engine.DefinitionId);
            if (block == null) return null;

            var name = ShortBlockName(block.DisplayName ?? block.Name);
            return report is { Runs: true } ? $"{name}, {report.Dyno!.MaxPowerHp:0} hp" : $"{name}, not running";
        }

        /// <summary>
        /// "Chrysler LA 340 engine Block" is a "Chrysler LA 340": the part's kind at the end of the name goes.
        /// A "GM 327-396 small block" keeps it, that is what the engine is called.
        /// </summary>
        private static string ShortBlockName(string name)
        {
            foreach (var kind in new[] { " engine block", " block" })
            {
                if (!name.EndsWith(kind, StringComparison.OrdinalIgnoreCase)) continue;

                var shorter = name[..^kind.Length].TrimEnd();
                var lastWord = shorter[(shorter.LastIndexOf(' ') + 1)..];
                var belongsToName = lastWord.ToLowerInvariant() is "small" or "big" or "bare" or "short";
                return belongsToName || !shorter.Contains(' ') ? name : shorter;
            }

            return name;
        }

        private BuiltEngine? CreateFactoryEngine(string carDefinitionId, double condition)
        {
            var definition = _catalogRepo.GetCar(carDefinitionId);
            var build = definition == null ? null : GetStockBuild(definition);
            var engine = build == null ? null : EngineFactory.CreateStock(Catalog, build, condition, Random.Shared);
            if (engine == null) _logger.Warning("No factory engine for {Car}", carDefinitionId);
            return engine;
        }

        /// <summary>Two screens may ask for the same car at the same moment: only the first engine goes in</summary>
        private bool Assign(Car car, BuiltEngine? engine)
        {
            if (engine == null) return false;

            lock (_assignLock)
            {
                if (car.HasPartsAssigned) return false;

                car.Parts.Add(engine.Root);
                car.HasPartsAssigned = true;
            }

            _logger.Information("{Car} given its factory engine: {Block}, {Power:0} hp", car.DefinitionId, engine.Root.DefinitionId,
                engine.Report.Dyno?.MaxPowerHp ?? 0);
            return true;
        }

        // A Lazy keeps the exception of a load that failed and throws it at everybody who asks afterwards.
        // Parts that cannot be read are parts that are not there: the game goes on without them.
        private PartsCatalog LoadCatalog()
        {
            try
            {
                var started = DateTime.Now;
                var catalog = PartsCatalog.Load(AppSettings.Instance.PartsPath);
                _logger.Information("Parts catalog: {Parts} parts, {Builds} engine builds in {Ms} ms", catalog.Parts.Count,
                    catalog.EngineBuilds.Count, (int)(DateTime.Now - started).TotalMilliseconds);
                return catalog;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not read the parts under {Path}: going without them", AppSettings.Instance.PartsPath);
                return PartsCatalog.Empty(AppSettings.Instance.PartsPath);
            }
        }

        private EngineBuildIndex CreateIndex()
        {
            try
            {
                var started = DateTime.Now;
                var index = EngineBuildIndex.Create(Catalog);
                _logger.Information("{Count} engine builds run, put on the dyno in {Ms} ms", index.Runnable.Count,
                    (int)(DateTime.Now - started).TotalMilliseconds);
                return index;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not put the engine builds on the dyno: going without them");
                return EngineBuildIndex.Empty;
            }
        }

        private RatedBuild? Suggest(CarDefinition car) =>
            StockEngineMatcher.Best(Builds, car.Brand, car.Name, StockEngineMatcher.ParsePower(car.Specs?.Bhp));

        /// <summary>
        /// Fills in the factory engine of every profile that has none, or whose build has left the catalog.
        /// An engine picked by hand is left alone even then: the car runs on the best match until somebody picks again.
        /// </summary>
        private void SuggestStockEngines()
        {
            foreach (var profile in _profileRepo.GetAllProfiles())
            {
                if (NeedsNoSuggestion(profile)) continue;

                var car = _catalogRepo.GetCar(profile.CarDefinitionId);
                var build = car == null ? null : Suggest(car);
                if (build == null) continue;

                // Onto the profile as it is in the database now, not as it was when the loop started: the
                // catalog editor may have saved it in the meantime
                var suggested = _profileRepo.UpdateProfile(profile.CarDefinitionId, stored =>
                {
                    if (NeedsNoSuggestion(stored)) return null;

                    stored.StockEngineBuildId = build.Build.Id;
                    stored.LastUpdatedDate = DateTime.Now;
                    return stored;
                });

                if (suggested) _logger.Information("Factory engine of {Car}: {Build} ({Power:0} hp)", profile.CarDefinitionId, build.Build.Name, build.PowerHp);
            }
        }

        private bool NeedsNoSuggestion(CarProfile profile) =>
            profile.StockEngineIsManual || Builds.Get(profile.StockEngineBuildId) != null;
    }
}
