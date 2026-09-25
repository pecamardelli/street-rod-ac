using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Services.Market;
using Street_Rod_AC.Services.Scheduler.Tasks;
using Street_Rod_AC.Services.Time;

namespace StreetRodAC.Tests;

/// <summary>Time that passes without a scheduler behind it</summary>
internal sealed class QuietTime : IGameTimeService
{
    public int DayStartHour => GameState.DayStartHour;
    public int DayEndHour => GameState.DayEndHour;
    public Task<TimeSpendResult> SpendTimeAsync(GameState gameState, GameAction action) => Task.FromResult(new TimeSpendResult());
    public Task<TimeSpendResult> SpendTimeAsync(GameState gameState, int minutes) => Task.FromResult(new TimeSpendResult());
    public int GetRemainingMinutesToday(GameState gameState) => 600;
    public double GetRemainingHoursToday(GameState gameState) => 10;
    public string GetFormattedTime(GameState gameState) => string.Empty;
    public string GetFormattedDate(GameState gameState) => string.Empty;
    public bool HasTimeFor(GameState gameState, GameAction action) => true;
    public Task<TimeSpendResult> EndDayAsync(GameState gameState) => Task.FromResult(new TimeSpendResult());
}

public class MarketTests
{
    private readonly MemoryCatalog _catalog = new();
    private readonly MemoryProfiles _profiles = new();
    private readonly UsedCarMarketService _market;

    public MarketTests()
    {
        TestLogging.SilenceLogging();
        _market = new UsedCarMarketService(_catalog, _profiles);
        _catalog.UpsertCar(new CarDefinition { Id = "car_a", Name = "A", Brand = "Ford" });
        _profiles.UpsertProfile(new CarProfile { CarDefinitionId = "car_a", BasePrice = 10000m });
    }

    [Fact]
    public void A_dented_car_sold_and_bought_back_off_the_lot_keeps_its_dents()
    {
        var car = new Car("car_a") { BodyDamageKmh = [30, 0, 12.5, 0] };
        car.BodyCondition = CarCondition.BodyCondition(car);

        var listing = _market.ListCar(car, 5000m, "industrial_motors", new DateTime(1971, 6, 1));
        var bought = CarPurchaseService.CarFrom(listing, new DateTime(1971, 6, 2), "Player");

        Assert.Equal(new double[] { 30, 0, 12.5, 0 }, bought.BodyDamageKmh);
        Assert.Equal(CarCondition.BodyCondition(car), bought.BodyCondition, 6);
        Assert.NotSame(car.BodyDamageKmh, bought.BodyDamageKmh);
    }

    [Fact]
    public void A_straight_car_and_an_older_listing_come_with_a_straight_body()
    {
        var listing = _market.ListCar(new Car("car_a"), 5000m, "industrial_motors", new DateTime(1971, 6, 1));
        Assert.Null(listing.BodyDamageKmh);

        var old = new UsedCarListing { CarDefinitionId = "car_a", Condition = 0.7f };
        var bought = CarPurchaseService.CarFrom(old, new DateTime(1971, 6, 2), "Player");
        Assert.Equal(0, CarCondition.BodyTotal(bought));
        Assert.Equal(0.7, bought.BodyCondition, 5);
    }

    [Fact]
    public void A_valuer_reads_each_model_from_the_catalog_once_and_values_like_ValueOf()
    {
        var cars = Enumerable.Range(0, 5).Select(_ => new Car("car_a") { EngineHealth = 0.5 }).ToList();
        var expected = _market.ValueOf(cars[0]);
        var profileReads = _profiles.Reads;
        var catalogReads = _catalog.Reads;

        var valueOf = _market.Valuer();
        Assert.All(cars, c => Assert.Equal(expected, valueOf(c)));

        Assert.Equal(1, _profiles.Reads - profileReads);
        Assert.Equal(1, _catalog.Reads - catalogReads);
    }

    [Fact]
    public void A_car_the_catalog_cannot_price_is_worth_what_was_paid_for_it()
    {
        var car = new Car("unknown") { PurchasePrice = 1234m };
        Assert.Equal(1200m, _market.ValueOf(car));
        Assert.Equal(1200m, _market.Valuer()(car));
    }

    [Fact]
    public async Task A_car_listed_while_the_market_refreshes_is_not_lost()
    {
        var state = GameState.CreateNew("P");
        state.DealerLocations = [new DealerLocation { Id = "downtown_motors" }];
        state.UsedCarMarket.Add(new UsedCarListing { Id = "old", DealerLocation = "downtown_motors", ListedDate = state.Date });

        var engines = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var market = new FakeMarket(0m)
        {
            Refresh = async listings =>
            {
                var kept = listings.ToList();
                await engines.Task;
                kept.Add(new UsedCarListing { Id = "fresh" });
                return kept;
            }
        };

        var refresh = new MarketRefreshTask(market).ExecuteAsync(state, state.Date);
        state.UsedCarMarket.Add(new UsedCarListing { Id = "traded_in" });
        engines.SetResult();
        await refresh;

        Assert.Equal(new[] { "old", "fresh", "traded_in" }, state.UsedCarMarket.Select(l => l.Id));
    }

    [Fact]
    public void A_zone_out_of_a_hand_edited_save_is_capped_and_the_repair_bill_is_a_number()
    {
        var car = new Car("car_a") { BodyDamageKmh = [1e29, double.NaN, -5, double.PositiveInfinity] };

        Assert.Equal(new double[] { CarCondition.MaxZoneKmh, 0, 0, 0 }, CarCondition.Body(car));

        var job = Assert.Single(RepairShop.Jobs(car, catalog: null));
        Assert.True(job.Cost > 0);
    }
}

public class CarPurchaseServiceTests
{
    private readonly GameState _state;
    private readonly UsedCarListing _listing;

    public CarPurchaseServiceTests()
    {
        TestLogging.SilenceLogging();
        _state = GameState.CreateNew("P");
        _state.Player.Money = 10000m;
        _listing = new UsedCarListing { Id = "l1", CarDefinitionId = "car_a", Price = 4000m, Condition = 0.8f };
        _state.UsedCarMarket.Add(_listing);
    }

    private static CarDefinition Definition => new() { Id = "car_a", Name = "A", Brand = "Ford" };

    [Fact]
    public async Task A_car_is_claimed_and_paid_for_before_anything_is_awaited()
    {
        var parts = new GatedParts();
        var service = new CarPurchaseService(new RaceResultProcessorTests.FakeRepository(), parts, new QuietTime());

        var first = service.PurchaseAsync(_state, _listing, Definition);
        Assert.True(_listing.IsSold);
        Assert.Equal(6000m, _state.Player.Money);

        // A second confirm while the first is putting the car together finds it gone
        var second = await service.PurchaseAsync(_state, _listing, Definition);
        Assert.Equal(PurchaseOutcome.NoLongerAvailable, second.Outcome);

        parts.Release.SetResult();
        var result = await first;
        Assert.True(result.Succeeded);
        Assert.Single(_state.Player.Cars);
        Assert.Equal(6000m, _state.Player.Money);
    }

    [Fact]
    public async Task A_purchase_that_fails_gives_back_the_money_and_the_listing()
    {
        var service = new CarPurchaseService(new RaceResultProcessorTests.FakeRepository(), new NoParts(), new QuietTime());
        _listing.Parts = null!; // a listing no car can be made from

        await Assert.ThrowsAnyAsync<Exception>(() => service.PurchaseAsync(_state, _listing, Definition));

        Assert.False(_listing.IsSold);
        Assert.Null(_listing.SoldDate);
        Assert.Equal(10000m, _state.Player.Money);
        Assert.Empty(_state.Player.Cars);
    }

    [Fact]
    public async Task Not_enough_money_buys_nothing()
    {
        _state.Player.Money = 100m;
        var service = new CarPurchaseService(new RaceResultProcessorTests.FakeRepository(), new NoParts(), new QuietTime());

        var result = await service.PurchaseAsync(_state, _listing, Definition);

        Assert.Equal(PurchaseOutcome.NotEnoughMoney, result.Outcome);
        Assert.False(_listing.IsSold);
        Assert.Equal(100m, _state.Player.Money);
    }
}
