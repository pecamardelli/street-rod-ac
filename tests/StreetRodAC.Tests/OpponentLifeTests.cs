using Street_Rod_AC.Models.Career.Milestones;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Parts;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Parts.Export;
using Street_Rod_AC.Parts.Logic;
using Street_Rod_AC.Parts.Sounds;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Market;
using Street_Rod_AC.Services.Opponents;
using Street_Rod_AC.Services.Parts;
using Street_Rod_AC.Services.Police;
using Street_Rod_AC.Services.Simulation;

namespace StreetRodAC.Tests;

public class OpponentRulesTests
{
    [Fact]
    public void Six_racers_start_and_two_more_come_every_week()
    {
        var start = GameState.GetStartingDateTime();
        Assert.Equal(6, OpponentRules.MinActive(start, 30));
        Assert.Equal(6, OpponentRules.MinActive(start.AddDays(6), 30));
        Assert.Equal(8, OpponentRules.MinActive(start.AddDays(7), 30));
        Assert.Equal(10, OpponentRules.MinActive(start.AddDays(20), 30));
        Assert.Equal(30, OpponentRules.MinActive(start.AddDays(400), 30));
        Assert.Equal(4, OpponentRules.MinActive(start, 4));
    }

    [Fact]
    public void A_car_that_can_race_beats_a_stronger_one_that_cannot()
    {
        Assert.True(OpponentRules.CarScore(150, 0.6, canRace: true, 1000m) > OpponentRules.CarScore(220, 0.9, canRace: false, 3000m));
        Assert.True(OpponentRules.CarScore(250, 0.6, true, 1000m) > OpponentRules.CarScore(150, 0.6, true, 1000m));
    }

    [Fact]
    public void A_buyer_wants_a_runner_and_money_left_over()
    {
        // Same car, one that runs
        Assert.True(OpponentRules.PurchaseScore(200, 0.7, true, 1500m, 3000m) > OpponentRules.PurchaseScore(200, 0.7, false, 1500m, 3000m));

        // Nearly the same power, one leaves money for parts
        Assert.True(OpponentRules.PurchaseScore(200, 0.7, true, 1500m, 3000m) > OpponentRules.PurchaseScore(210, 0.7, true, 2900m, 3000m));
    }

    [Fact]
    public void Broke_means_no_racing_car_and_less_than_the_cheapest_car()
    {
        Assert.True(OpponentRules.IsBankrupt(false, 300m, 500m));
        Assert.False(OpponentRules.IsBankrupt(false, 600m, 500m));
        Assert.False(OpponentRules.IsBankrupt(true, 0m, 500m));
        Assert.True(OpponentRules.IsBankrupt(false, 150m, null)); // nothing for sale: under $200 is broke
    }

    [Fact]
    public void Tuning_money_is_a_share_of_what_is_over_a_reserve_that_follows_the_cars_worth()
    {
        Assert.Equal(0m, OpponentRules.TuningBudget(400m, 2000m)); // a quarter of the car kept back
        Assert.Equal(150m, OpponentRules.TuningBudget(1000m, 2000m));
        Assert.Equal(1500m, OpponentRules.TuningBudget(10000m, 20000m)); // ten times the prices, ten times the money
    }

    [Fact]
    public void A_broke_racer_scrapes_together_a_share_of_the_cheapest_car()
    {
        Assert.Equal(100m, OpponentRules.CashInjection(1000m, 0));
        Assert.Equal(600m, OpponentRules.CashInjection(1000m, 1));
        Assert.Equal(6000m, OpponentRules.CashInjection(10000m, 1));
        Assert.InRange(OpponentRules.CashInjection(null, 0.5), OpponentRules.MinCashInjection, OpponentRules.MaxCashInjection);
    }
}

public class OpponentLifeServiceTests
{
    private sealed class NoCatalog : IContentCatalogRepository
    {
        public void UpsertCar(CarDefinition car) { }
        public void UpsertCars(IEnumerable<CarDefinition> cars) { }
        public CarDefinition? GetCar(string id) => null;
        public List<CarDefinition> GetAllCars() => [];
        public List<CarDefinition> GetCarsByStatus(ContentStatus status) => [];
        public bool CarExists(string id) => false;
        public void UpdateCarStatus(string id, ContentStatus status) { }
        public void MarkAllCarsAsLegacy() { }
        public void DeleteBrokenCars() { }
        public int GetCarCount() => 0;
    }

    private readonly FakeMarket _market = new(1000m);
    private readonly OpponentLifeService _life;
    private readonly GameState _state;

    public OpponentLifeServiceTests()
    {
        TestLogging.SilenceLogging();
        _life = new OpponentLifeService(new NoParts(), _market, new NoCatalog());
        _state = GameState.CreateNew("P");
    }

    private static Car GoodCar(string id = "car_good", double hp = 200) => new(id) { PowerHp = hp };

    private Opponent Racer(string name, decimal money, RacerStatus status, params Car[] cars)
    {
        var racer = new Opponent(name, 30, Gender.Female, 92, 50) { Money = money, Status = status };
        racer.Cars.AddRange(cars);
        _state.Racers.AddRacer(racer);
        return racer;
    }

    private UsedCarListing Listing(string id, decimal price, double? hp, float condition = 0.8f)
    {
        var listing = new UsedCarListing { CarDefinitionId = id, Price = price, PowerHp = hp, Condition = condition, DealerLocation = "downtown_motors" };
        _state.UsedCarMarket.Add(listing);
        return listing;
    }

    [Fact]
    public async Task A_racer_without_a_car_buys_the_best_one_they_can_afford_off_the_lot()
    {
        var racer = Racer("Ann", 2000m, RacerStatus.Retired);
        var dear = Listing("car_dear", 5000m, 400);
        var runner = Listing("car_runner", 900m, 180);
        var dud = Listing("car_dud", 700m, 0);

        await _life.ReviewDayAsync(_state, _state.Date);

        var car = Assert.Single(racer.Cars);
        Assert.Equal("car_runner", car.DefinitionId);
        Assert.Equal(180, car.PowerHp);
        Assert.True(runner.IsSold);
        Assert.False(dear.IsSold || dud.IsSold);
        Assert.Equal(1100m, racer.Money);
        Assert.Equal(RacerStatus.ReadyToRace, racer.Status);
        Assert.Contains(_state.StreetTalk, t => t.Text.Contains("Ann bought"));
    }

    [Fact]
    public async Task Too_many_cars_with_every_spare_in_the_impound_waits_and_the_day_goes_on()
    {
        var driven = GoodCar();
        var spares = Enumerable.Range(0, OpponentRules.MaxCars).Select(i => new Car($"car_impounded_{i}") { ImpoundedUntil = _state.Date.AddDays(30) }).ToArray();
        var racer = Racer("Ivy", 500m, RacerStatus.Retired, [driven, .. spares]);

        await _life.ReviewDayAsync(_state, _state.Date);

        Assert.Equal(OpponentRules.MaxCars + 1, racer.Cars.Count);
        Assert.Same(driven, racer.Cars[0]);
        Assert.Empty(_market.Listed);
        Assert.Equal(RacerStatus.ReadyToRace, racer.Status); // the rest of the review ran
    }

    [Fact]
    public async Task A_wreck_is_fixed_when_the_whole_bill_is_affordable()
    {
        var wreck = GoodCar();
        wreck.BodyDamageKmh = new double[] { 210, 0, 0, 0 };
        var racer = Racer("Bo", 5000m, RacerStatus.ReadyToRace, wreck);

        await _life.ReviewDayAsync(_state, _state.Date);

        Assert.False(CarCondition.IsTotaled(wreck));
        Assert.True(racer.Money < 5000m);
        Assert.Equal(RacerStatus.ReadyToRace, racer.Status);
    }

    [Fact]
    public async Task A_wreck_too_dear_to_fix_is_sold_for_scrap_and_a_runner_bought_with_the_money()
    {
        var wreck = GoodCar();
        wreck.BodyDamageKmh = new double[] { 210, 0, 0, 0 };
        var racer = Racer("Cy", 300m, RacerStatus.ReadyToRace, wreck);
        Listing("car_cheap", 400m, 120);

        await _life.ReviewDayAsync(_state, _state.Date);

        var car = Assert.Single(racer.Cars);
        Assert.Equal("car_cheap", car.DefinitionId);
        Assert.Empty(_market.Listed); // a wreck is scrapped, not put on a lot
        Assert.Equal(RacerStatus.ReadyToRace, racer.Status);
    }

    [Fact]
    public async Task A_broke_racer_scrapes_money_together_and_sits_it_out_until_they_can_buy()
    {
        var racer = Racer("Dee", 10m, RacerStatus.ReadyToRace);
        Listing("car_x", 2000m, 150);

        await _life.ReviewDayAsync(_state, _state.Date);

        Assert.InRange(racer.Money, 10m + 2000m * (decimal)OpponentRules.MinCashShare, 10m + 2000m * (decimal)OpponentRules.MaxCashShare);
        Assert.Equal(RacerStatus.Retired, racer.Status);
        Assert.Contains(_state.StreetTalk, t => t.Text.Contains("broke"));
    }

    [Fact]
    public async Task More_racers_come_out_as_the_weeks_go_by()
    {
        for (var i = 0; i < 12; i++) Racer($"R{i}", 1000m, RacerStatus.Inactive, GoodCar());
        var twoWeeksIn = GameState.GetStartingDateTime().AddDays(14);

        await _life.ReviewDayAsync(_state, twoWeeksIn);

        Assert.Equal(10, _state.Racers.ReadyToRace.Count);
        Assert.Equal(2, _state.Racers.Inactive.Count);
        Assert.Contains(_state.StreetTalk, t => t.Text.StartsWith("A new face at the diner"));
    }

    [Fact]
    public async Task A_racer_drives_their_best_car_and_sells_a_third()
    {
        var weak = GoodCar("car_weak", 120);
        var strong = GoodCar("car_strong", 300);
        var middle = GoodCar("car_mid", 200);
        var racer = Racer("Eve", 1000m, RacerStatus.ReadyToRace, weak, strong, middle);

        await _life.ReviewDayAsync(_state, _state.Date);

        Assert.Equal(new[] { strong, middle }, racer.Cars);
        Assert.Equal(new[] { weak }, _market.Listed.Select(l => l.Car));
        Assert.Equal(1000m + 600m, racer.Money); // a dealer's 60%
        Assert.Single(_state.UsedCarMarket);
    }

    [Fact]
    public async Task The_King_stays_out_of_sight_until_the_player_has_earned_a_shot()
    {
        var king = Racer("The King", 20000m, RacerStatus.Inactive, GoodCar("car_king", 500));
        king.IsKing = true;
        for (var i = 0; i < 3; i++) Racer($"R{i}", 1000m, RacerStatus.Inactive, GoodCar());

        await _life.ReviewDayAsync(_state, _state.Date);
        Assert.Equal(RacerStatus.Inactive, king.Status);
        Assert.Equal(3, _state.Racers.ReadyToRace.Count); // everybody else came out, not him

        _state.Career.SetCounter(MilestoneTrigger.TotalWins, 10);
        _state.Career.SetCounter(MilestoneTrigger.ReputationReached, 50);
        await _life.ReviewDayAsync(_state, _state.Date.AddDays(1));
        Assert.Equal(RacerStatus.ReadyToRace, king.Status);
    }

    [Fact]
    public void The_street_forgets_after_a_week()
    {
        var day = _state.Date;
        OpponentLifeService.AddTalk(_state, day, ["old news"]);
        OpponentLifeService.AddTalk(_state, day.AddDays(OpponentLifeService.StreetTalkDays + 1), ["news"]);

        Assert.Equal(new[] { "news" }, _state.StreetTalk.Select(t => t.Text));
    }

    [Fact]
    public async Task A_racer_waits_for_a_car_in_the_impound_and_collects_it_when_its_days_are_up()
    {
        var car = GoodCar();
        var racer = Racer("Eve", 2000m, RacerStatus.ReadyToRace, car);
        Listing("car_cheap", 400m, 120);
        PoliceRules.Impound(car, _state.Date, earlierBusts: 0);

        // In the impound: not fixed, not sold, not replaced; they sit it out
        await _life.ReviewDayAsync(_state, _state.Date);
        Assert.Same(car, Assert.Single(racer.Cars));
        Assert.True(car.IsImpounded);
        Assert.Equal(RacerStatus.Retired, racer.Status);
        Assert.Contains(_state.StreetTalk, t => t.Text.Contains("impound"));

        // Its days up: paid for and back on the street
        var fee = car.ImpoundFee;
        await _life.ReviewDayAsync(_state, car.ImpoundedUntil!.Value);
        Assert.False(car.IsImpounded);
        Assert.Equal(2000m - fee, racer.Money);
        Assert.Equal(RacerStatus.ReadyToRace, racer.Status);
    }

    [Fact]
    public async Task A_car_nobody_pays_for_is_auctioned_off_by_the_police()
    {
        var car = GoodCar();
        var racer = Racer("Fay", 0m, RacerStatus.Retired, car);
        PoliceRules.Impound(car, _state.Date, earlierBusts: 0);
        car.ImpoundFee = 1_000_000m;

        await _life.ReviewDayAsync(_state, car.ImpoundedUntil!.Value.AddDays(PoliceRules.AuctionAfterDays));

        Assert.DoesNotContain(car, racer.Cars);
        Assert.Contains(_state.StreetTalk, t => t.Text.Contains("auctioned"));
    }
}

public class RaceSimulationWearTests
{
    [Fact]
    public void A_crash_writes_the_body_off()
    {
        var car = new Car("car") { PowerHp = 200 };
        var random = new Random(4);

        var condition = RaceSimulatorService.SimulatedCondition(car, isRoadRace: true, won: false, 1.0, crashed: true, null, random);
        CarCondition.ApplyRace(car, condition, 30, null);

        Assert.True(CarCondition.BodyTotal(car) >= CarCondition.TotaledKmh * 0.7);
    }

    [Fact]
    public void A_clean_race_wears_a_little_and_the_car_still_races()
    {
        var car = new Car("car") { PowerHp = 200 };
        var random = new Random(5);

        for (var i = 0; i < 5; i++)
        {
            var condition = RaceSimulatorService.SimulatedCondition(car, isRoadRace: false, won: true, 1.0, crashed: false, null, random);
            CarCondition.ApplyRace(car, condition, 10, null);
        }

        Assert.Empty(CarCondition.WhyCannotRace(car, null));
        Assert.True(car.EngineHealth < 1.0);
    }

    [Fact]
    public void The_simulator_races_by_the_dyno_and_falls_back_on_the_price()
    {
        Assert.Equal(250, RaceSimulatorService.GetCarHP(new Car("a") { PowerHp = 250 }));
        Assert.Equal(0, RaceSimulatorService.GetCarHP(new Car("a") { PowerHp = 0 }));
        Assert.Equal(120, RaceSimulatorService.GetCarHP(new Car("a") { PurchasePrice = 1000m }));
    }
}
