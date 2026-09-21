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

            var definition = _catalogRepo.GetCar(car.DefinitionId);
            var build = definition == null ? null : GetStockBuild(definition);
            var engine = build == null ? null : EngineFactory.CreateStock(Catalog, build, car.EngineHealth, Random.Shared);
            if (engine == null)
            {
                _logger.Warning("No factory engine for {Car}", car.DefinitionId);
                return false;
            }

            car.Parts.Add(engine.Root);
            car.HasPartsAssigned = true;
            _logger.Information("{Car} given its factory engine: {Build}, {Power:0} hp", car.DefinitionId, build!.Build.Name,
                engine.Report.Dyno?.MaxPowerHp ?? 0);
            return true;
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

        public EngineReport? Evaluate(Car car) =>
            IsAvailable && car.Engine is { } engine ? EngineFactory.Evaluate(Catalog, engine) : null;

        public string? Describe(PartInstance engine, EngineReport? report)
        {
            var block = Catalog.Get(engine.DefinitionId);
            if (block == null) return null;

            var name = (block.DisplayName ?? block.Name).Replace(" engine Block", "", StringComparison.OrdinalIgnoreCase)
                .Replace(" block", "", StringComparison.OrdinalIgnoreCase);
            return report is { Runs: true } ? $"{name}, {report.Dyno!.MaxPowerHp:0} hp" : $"{name}, not running";
        }

        private PartsCatalog LoadCatalog()
        {
            var started = DateTime.Now;
            var catalog = PartsCatalog.Load(AppSettings.Instance.PartsPath);
            _logger.Information("Parts catalog: {Parts} parts, {Builds} engine builds in {Ms} ms", catalog.Parts.Count,
                catalog.EngineBuilds.Count, (int)(DateTime.Now - started).TotalMilliseconds);
            return catalog;
        }

        private EngineBuildIndex CreateIndex()
        {
            var started = DateTime.Now;
            var index = EngineBuildIndex.Create(Catalog);
            _logger.Information("{Count} engine builds run, put on the dyno in {Ms} ms", index.Runnable.Count,
                (int)(DateTime.Now - started).TotalMilliseconds);
            return index;
        }

        private RatedBuild? Suggest(CarDefinition car) =>
            StockEngineMatcher.Best(Builds, car.Brand, car.Name, StockEngineMatcher.ParsePower(car.Specs?.Bhp));

        /// <summary>Fills in the factory engine of every profile that has none, or whose build has left the catalog</summary>
        private void SuggestStockEngines()
        {
            foreach (var profile in _profileRepo.GetAllProfiles())
            {
                if (Builds.Get(profile.StockEngineBuildId) != null) continue;

                var car = _catalogRepo.GetCar(profile.CarDefinitionId);
                var build = car == null ? null : Suggest(car);
                if (build == null) continue;

                profile.StockEngineBuildId = build.Build.Id;
                profile.StockEngineIsManual = false;
                profile.LastUpdatedDate = DateTime.Now;
                _profileRepo.UpsertProfile(profile);
                _logger.Information("Factory engine of {Car}: {Build} ({Power:0} hp)", profile.CarDefinitionId, build.Build.Name, build.PowerHp);
            }
        }
    }
}
