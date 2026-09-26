using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Parts.Export;
using Street_Rod_AC.Parts.Logic;
using Street_Rod_AC.Parts.Sounds;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Market;
using Street_Rod_AC.Services.Parts;

namespace StreetRodAC.Tests;

/// <summary>
/// A market that values every car at <see cref="Value"/> and writes down what is put on a lot, for the services
/// that sell cars (the player's sales, the rivals' days)
/// </summary>
internal sealed class FakeMarket(decimal value, List<DealerLocation>? dealers = null) : IUsedCarMarketService
{
    public decimal Value { get; set; } = value;
    public List<(Car Car, decimal Price, string Location)> Listed { get; } = [];

    public decimal ValueOf(Car car) => Value;
    public string TradeInLocation(IReadOnlyList<DealerLocation>? known) => "industrial_motors";
    public List<DealerLocation> GetDefaultDealers() => dealers ?? [];

    public UsedCarListing ListCar(Car car, decimal price, string location, DateTime listedDate, bool describeEngine = true)
    {
        Listed.Add((car, price, location));
        return new UsedCarListing { CarDefinitionId = car.DefinitionId, Price = price, DealerLocation = location, ListedDate = listedDate };
    }

    public EngineDescription? DescribeEngine(Car car) => null;

    public Task<List<UsedCarListing>> SpawnListingsAsync(List<DealerLocation> dealers, DateTime currentDate, double priceMultiplier) => throw new NotSupportedException();
    /// <summary>What a refresh does with the listings there are; unset, a refresh is not expected</summary>
    public Func<List<UsedCarListing>, Task<List<UsedCarListing>>>? Refresh { get; set; }

    public Task<List<UsedCarListing>> RefreshMarketAsync(List<UsedCarListing> currentListings, List<DealerLocation> dealers, DateTime currentDate, double priceMultiplier) =>
        Refresh?.Invoke(currentListings) ?? throw new NotSupportedException();
    public List<UsedCarListing> GetAvailableListings(List<UsedCarListing> allListings) => allListings;
    public List<UsedCarListing> GetListingsByDealer(List<UsedCarListing> allListings, string dealerLocationId) => allListings;
}

/// <summary>No parts catalog: the cars are judged on their own figures, the body shop still works</summary>
internal sealed class NoParts : ICarPartsService
{
    public PartsCatalog Catalog => throw new NotSupportedException();
    public EngineBuildIndex Builds => throw new NotSupportedException();
    public bool IsAvailable => false;
    public Task WarmUpAsync() => Task.CompletedTask;
    public RatedBuild? GetStockBuild(CarDefinition car) => null;
    public bool EnsureParts(Car car) => false;
    public Task<bool> EnsurePartsAsync(Car car) => Task.FromResult(false);
    public bool BringUpToDate(GameState game) => false;
    public BuiltEngine? CreateUsedEngine(CarDefinition car, double condition) => null;
    public string? Describe(PartInstance engine, EngineReport? report) => null;
    public EngineReport? Evaluate(Car car) => null;
    public Street_Rod_AC.Models.Race.EngineCoolingRating? RateCooling(Car car, EngineReport? engine) => null;
    public SoundLibrary Sounds => throw new NotSupportedException();
    public CarSound? ChooseSound(Car car, EngineReport? report) => null;
    public double? FactoryEngineMass(Car car) => null;
    public AcCarSpecs? Specs(string carDefinitionId) => null;
    public (RunningGearFactory.AxleParts Front, RunningGearFactory.AxleParts Rear)? FactoryRunningGear(Car car) => null;
}

/// <summary>No parts catalog, and putting parts on a car takes until the test says so</summary>
internal sealed class GatedParts : ICarPartsService
{
    private readonly NoParts _none = new();

    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<bool> EnsurePartsAsync(Car car)
    {
        await Release.Task;
        return false;
    }

    public PartsCatalog Catalog => _none.Catalog;
    public EngineBuildIndex Builds => _none.Builds;
    public bool IsAvailable => false;
    public Task WarmUpAsync() => Task.CompletedTask;
    public RatedBuild? GetStockBuild(CarDefinition car) => null;
    public bool EnsureParts(Car car) => false;
    public bool BringUpToDate(GameState game) => false;
    public BuiltEngine? CreateUsedEngine(CarDefinition car, double condition) => null;
    public string? Describe(PartInstance engine, EngineReport? report) => null;
    public EngineReport? Evaluate(Car car) => null;
    public Street_Rod_AC.Models.Race.EngineCoolingRating? RateCooling(Car car, EngineReport? engine) => null;
    public SoundLibrary Sounds => _none.Sounds;
    public CarSound? ChooseSound(Car car, EngineReport? report) => null;
    public double? FactoryEngineMass(Car car) => null;
    public AcCarSpecs? Specs(string carDefinitionId) => null;
    public (RunningGearFactory.AxleParts Front, RunningGearFactory.AxleParts Rear)? FactoryRunningGear(Car car) => null;
}

/// <summary>A car catalog held in memory: whatever the test puts in <see cref="Cars"/>; counts its reads</summary>
internal sealed class MemoryCatalog : IContentCatalogRepository
{
    public Dictionary<string, CarDefinition> Cars { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int Reads { get; private set; }

    public void UpsertCar(CarDefinition car) => Cars[car.Id] = car;
    public void UpsertCars(IEnumerable<CarDefinition> cars) { foreach (var car in cars) UpsertCar(car); }
    public CarDefinition? GetCar(string id) { Reads++; return Cars.GetValueOrDefault(id); }
    public List<CarDefinition> GetAllCars() { Reads++; return [.. Cars.Values]; }
    public List<CarDefinition> GetCarsByStatus(ContentStatus status) { Reads++; return [.. Cars.Values.Where(c => c.Status == status)]; }
    public bool CarExists(string id) => Cars.ContainsKey(id);
    public void UpdateCarStatus(string id, ContentStatus status) { if (Cars.TryGetValue(id, out var car)) car.Status = status; }
    public void MarkAllCarsAsLegacy() { }
    public void DeleteBrokenCars() { }
    public int GetCarCount() => Cars.Count;
}

/// <summary>Car profiles held in memory; counts its reads</summary>
internal sealed class MemoryProfiles : ICarProfileRepository
{
    public Dictionary<string, CarProfile> Profiles { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int Reads { get; private set; }

    public CarProfile? GetProfile(string carDefinitionId) { Reads++; return Profiles.GetValueOrDefault(carDefinitionId); }
    public CarProfile GetOrCreateProfile(string carDefinitionId, Func<CarProfile> createDefault) =>
        Profiles.TryGetValue(carDefinitionId, out var profile) ? profile : Profiles[carDefinitionId] = createDefault();
    public void UpsertProfile(CarProfile profile) => Profiles[profile.CarDefinitionId] = profile;
    public void UpsertProfiles(IEnumerable<CarProfile> profiles) { foreach (var profile in profiles) UpsertProfile(profile); }

    public bool UpdateProfile(string carDefinitionId, Func<CarProfile, CarProfile?> change)
    {
        if (!Profiles.TryGetValue(carDefinitionId, out var stored) || change(stored) is not { } changed) return false;
        Profiles[carDefinitionId] = changed;
        return true;
    }

    public int MergeProfiles(IEnumerable<CarProfile> created, IEnumerable<KeyValuePair<string, Func<CarProfile, CarProfile?>>> changes)
    {
        var stored = 0;
        foreach (var profile in created)
        {
            if (Profiles.TryAdd(profile.CarDefinitionId, profile)) stored++;
        }

        foreach (var (id, change) in changes)
        {
            if (UpdateProfile(id, change)) stored++;
        }

        return stored;
    }

    public List<CarProfile> GetAllProfiles() { Reads++; return [.. Profiles.Values]; }
    public bool ProfileExists(string carDefinitionId) => Profiles.ContainsKey(carDefinitionId);
    public int GetProfileCount() => Profiles.Count;
}
