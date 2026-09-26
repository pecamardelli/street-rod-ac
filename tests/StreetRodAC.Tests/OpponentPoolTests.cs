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

namespace StreetRodAC.Tests;

/// <summary>A dice that always rolls the same: 0.99 never gives a racer a reason to act on chance</summary>
internal sealed class FixedRandom(double roll) : Random
{
    public override double NextDouble() => roll;
}

/// <summary>The real parts catalog and nothing else</summary>
internal sealed class CatalogParts : ICarPartsService
{
    private static readonly Lazy<PartsCatalog> Loaded = new(() => PartsCatalog.Load(RepoPaths.PartsFolder));
    private static readonly Lazy<EngineBuildIndex> Index = new(() => EngineBuildIndex.Create(Loaded.Value));

    public PartsCatalog Catalog => Loaded.Value;
    public EngineBuildIndex Builds => Index.Value;
    public bool IsAvailable => true;
    public Task WarmUpAsync() => Task.CompletedTask;
    public RatedBuild? GetStockBuild(CarDefinition car) => null;
    public bool EnsureParts(Car car) => false;
    public Task<bool> EnsurePartsAsync(Car car) => Task.FromResult(false);
    public bool BringUpToDate(GameState game) => false;
    public BuiltEngine? CreateUsedEngine(CarDefinition car, double condition) => null;
    public string? Describe(PartInstance engine, EngineReport? report) => null;
    public EngineReport? Evaluate(Car car) => null;
    public EngineCoolingRating? RateCooling(Car car, EngineReport? engine) => null;
    public SoundLibrary Sounds => throw new NotSupportedException();
    public CarSound? ChooseSound(Car car, EngineReport? report) => null;
    public double? FactoryEngineMass(Car car) => null;
    public AcCarSpecs? Specs(string carDefinitionId) => null;
    public (RunningGearFactory.AxleParts Front, RunningGearFactory.AxleParts Rear)? FactoryRunningGear(Car car) => null;
}

public class OpponentPoolRulesTests
{
    [Fact]
    public void A_racer_leaves_after_going_broke_once_too_often_or_laid_up_too_long_and_rarely_otherwise()
    {
        Assert.True(OpponentRules.Leaves(OpponentRules.MaxTimesBroke, null, 0.99));
        Assert.False(OpponentRules.Leaves(OpponentRules.MaxTimesBroke - 1, null, 0.99));

        // Laid up: a fair chance each day once it has been long enough
        Assert.True(OpponentRules.Leaves(0, OpponentRules.LaidUpDays, OpponentRules.LaidUpLeaveChance / 2));
        Assert.False(OpponentRules.Leaves(0, OpponentRules.LaidUpDays - 1, OpponentRules.LaidUpLeaveChance / 2));

        // Moving on: rare
        Assert.True(OpponentRules.Leaves(0, null, OpponentRules.LeaveChance / 2));
        Assert.False(OpponentRules.Leaves(0, null, OpponentRules.LeaveChance * 2));
    }

    [Fact]
    public void Trading_up_takes_a_clearly_stronger_car_and_money_left_over()
    {
        Assert.True(OpponentRules.WorthTradingUp(200, 250, 2000m, 3000m));
        Assert.False(OpponentRules.WorthTradingUp(200, 220, 2000m, 3000m)); // not enough more
        Assert.False(OpponentRules.WorthTradingUp(200, 300, 2000m, 2200m)); // nothing left over
        Assert.True(OpponentRules.WorthTradingUp(0, 150, 1000m, 2000m));    // anything beats a car with no figure
    }

    [Fact]
    public void A_rival_asks_less_than_a_lot_and_never_less_than_a_dealer_pays()
    {
        Assert.Equal(7500m, OpponentRules.AskingPrice(10000m, 6000m, 0));
        Assert.Equal(9000m, OpponentRules.AskingPrice(10000m, 6000m, 1));
        Assert.Equal(8000m, OpponentRules.AskingPrice(10000m, 8000m, 0));
    }
}

public class OpponentPoolLifeTests
{
    private readonly FakeMarket _market = new(1000m);
    private readonly GameState _state;

    public OpponentPoolLifeTests()
    {
        TestLogging.SilenceLogging();
        _state = GameState.CreateNew("P");
    }

    private OpponentLifeService Life(double roll = 0.99) => new(new NoParts(), _market, new MemoryCatalog(), random: new FixedRandom(roll));

    private static Car GoodCar(string id = "car_good", double hp = 200) => new(id) { PowerHp = hp };

    private Opponent Racer(string name, decimal money, RacerStatus status, params Car[] cars)
    {
        var racer = new Opponent(name, 30, Gender.Female, 92, 50) { Money = money, Status = status };
        racer.Cars.AddRange(cars);
        _state.Racers.AddRacer(racer);
        return racer;
    }

    [Fact]
    public async Task A_spare_too_many_goes_in_the_paper_and_stays_in_the_garage_until_it_sells()
    {
        var weak = GoodCar("car_weak", 120);
        var strong = GoodCar("car_strong", 300);
        var middle = GoodCar("car_mid", 200);
        var racer = Racer("Eve", 1000m, RacerStatus.ReadyToRace, weak, strong, middle);

        await Life().ReviewDayAsync(_state, _state.Date);

        Assert.Same(strong, racer.Cars[0]);
        Assert.Contains(weak, racer.Cars);
        Assert.Empty(_market.Listed);
        Assert.Equal(1000m, racer.Money);

        var ad = Assert.Single(_state.NewspaperAds.RivalCars);
        Assert.Equal(weak.InstanceId, ad.CarInstanceId);
        Assert.InRange(ad.AskingPrice, 600m, 1000m);
        Assert.Contains(_state.StreetTalk, t => t.Text.Contains("in the paper"));
    }

    [Fact]
    public async Task An_ad_nobody_answers_comes_down_in_price_then_goes_to_a_dealer()
    {
        var spare = GoodCar("car_spare", 120);
        var racer = Racer("Eve", 1000m, RacerStatus.ReadyToRace, GoodCar(), spare);
        var ad = RivalCarAds.Post(_state, racer, spare, 900m, _state.Date);
        var life = Life();

        await life.ReviewDayAsync(_state, _state.Date.AddDays(OpponentRules.AskReducedAfterDays));
        Assert.True(ad.Reduced);
        Assert.Equal(800m, ad.AskingPrice); // 810 to the hundred

        await life.ReviewDayAsync(_state, _state.Date.AddDays(CarSaleService.AdDays));
        Assert.DoesNotContain(spare, racer.Cars);
        Assert.Empty(_state.NewspaperAds.RivalCars);
        Assert.Equal(spare, Assert.Single(_market.Listed).Car);
        Assert.Equal(1600m, racer.Money);
    }

    [Fact]
    public async Task A_racer_without_a_car_buys_another_racers_car_out_of_the_paper()
    {
        var spare = GoodCar("car_spare", 250);
        var seller = Racer("Sal", 100m, RacerStatus.ReadyToRace, GoodCar(), spare);
        RivalCarAds.Post(_state, seller, spare, 700m, _state.Date);
        var buyer = Racer("Bea", 1000m, RacerStatus.Retired);

        await Life().ReviewDayAsync(_state, _state.Date);

        Assert.Same(spare, Assert.Single(buyer.Cars));
        Assert.DoesNotContain(spare, seller.Cars);
        Assert.Equal(300m, buyer.Money);
        Assert.Equal(800m, seller.Money);
        Assert.Empty(_state.NewspaperAds.RivalCars);
        Assert.Equal(CarAcquisition.PrivateSale, spare.History.Owners[^1].How);
        Assert.Equal("Bea", spare.History.Owners[^1].Name);
        Assert.Contains(_state.StreetTalk, t => t.Text.Contains("out of the paper"));
    }

    [Fact]
    public async Task A_racer_with_money_trades_up_and_puts_the_old_car_in_the_paper()
    {
        var old = GoodCar("car_old", 150);
        var racer = Racer("Tom", 5000m, RacerStatus.ReadyToRace, old);
        var listing = new UsedCarListing { CarDefinitionId = "car_new", Price = 2000m, PowerHp = 260, Condition = 0.8f, DealerLocation = "downtown_motors" };
        _state.UsedCarMarket.Add(listing);

        // A roll under the trade-up chance and over the chance of leaving
        await Life(0.03).ReviewDayAsync(_state, _state.Date);

        Assert.True(listing.IsSold);
        Assert.Equal("car_new", racer.Cars[0].DefinitionId);
        Assert.Equal(3000m, racer.Money);
        Assert.Equal(old.InstanceId, Assert.Single(_state.NewspaperAds.RivalCars).CarInstanceId);
    }

    [Fact]
    public async Task Broke_one_time_too_many_they_sell_up_and_leave_the_scene()
    {
        var wreck = GoodCar();
        wreck.BodyDamageKmh = new double[] { 210, 0, 0, 0 };
        var racer = Racer("Dee", 10m, RacerStatus.Retired, wreck);
        racer.TimesBroke = OpponentRules.MaxTimesBroke - 1;
        _state.UsedCarMarket.Add(new UsedCarListing { CarDefinitionId = "car_x", Price = 2000m, PowerHp = 150, DealerLocation = "downtown_motors" });

        await Life().ReviewDayAsync(_state, _state.Date);

        Assert.Equal(RacerStatus.Departed, racer.Status);
        Assert.DoesNotContain(racer, _state.Racers.All);
        Assert.Null(_state.Racers.Find("Dee"));
        Assert.Empty(racer.Cars);
        Assert.Equal(_state.Date, racer.LeftDate);
        Assert.Contains(_state.StreetTalk, t => t.Text.Contains("quit the scene"));
    }

    [Fact]
    public async Task A_spell_of_being_broke_counts_once_however_long_it_lasts()
    {
        var racer = Racer("Dee", 10m, RacerStatus.Retired);
        _state.UsedCarMarket.Add(new UsedCarListing { CarDefinitionId = "car_x", Price = 2000m, PowerHp = 150, Condition = 0.8f, DealerLocation = "downtown_motors" });
        var life = Life(0.5); // $700 a day scraped together: three days broke before the car is in reach

        for (var day = 0; day < 3; day++) await life.ReviewDayAsync(_state, _state.Date.AddDays(day));

        Assert.Equal(1, racer.TimesBroke);
        Assert.True(racer.IsBroke);
        Assert.NotEqual(RacerStatus.Departed, racer.Status);

        // Back on the street, then broke again: that is the second time
        await life.ReviewDayAsync(_state, _state.Date.AddDays(3));
        Assert.False(racer.IsBroke);
        Assert.NotEmpty(racer.Cars);
    }

    [Fact]
    public async Task A_racer_with_a_race_on_or_a_grudge_does_not_leave()
    {
        var pending = Racer("Pat", 10m, RacerStatus.Retired);
        pending.TimesBroke = OpponentRules.MaxTimesBroke;
        _state.PendingRace = new RaceContext { OpponentName = "Pat" };

        var sore = Racer("Sid", 10m, RacerStatus.Retired);
        sore.TimesBroke = OpponentRules.MaxTimesBroke;
        sore.Grudge = new Grudge { Until = _state.Date.AddDays(10), CarDefinitionId = "car" };

        await Life().ReviewDayAsync(_state, _state.Date);

        Assert.NotEqual(RacerStatus.Departed, pending.Status);
        Assert.NotEqual(RacerStatus.Departed, sore.Status);
        Assert.True(pending.Money > 10m); // they scraped some money together instead
    }

    [Fact]
    public async Task With_no_new_faces_left_a_racer_who_left_long_ago_comes_back()
    {
        var gone = new Opponent("Gus", 50, Gender.Male, 92, 40) { Status = RacerStatus.Departed, LeftDate = _state.Date, TimesBroke = 3 };
        gone.Cars.Add(GoodCar());
        _state.Racers.AddRacer(gone);
        var later = GameState.GetStartingDateTime().AddDays(OpponentRules.ComeBackAfterDays);

        await Life().ReviewDayAsync(_state, later);

        Assert.NotEqual(RacerStatus.Departed, gone.Status);
        Assert.Contains(gone, _state.Racers.All);
        Assert.Equal(0, gone.TimesBroke);
        Assert.Null(gone.LeftDate);
        Assert.True(gone.Money > 0);
        Assert.Contains(_state.StreetTalk, t => t.Text.Contains("back in town"));

        // Back, not new
        Assert.Equal(RacerStatus.ReadyToRace, gone.Status);
        Assert.DoesNotContain(_state.StreetTalk, t => t.Text.Contains("new face"));
    }

    [Fact]
    public async Task Not_back_before_their_time()
    {
        var gone = new Opponent("Gus", 50, Gender.Male, 92, 40) { Status = RacerStatus.Departed, LeftDate = _state.Date };
        _state.Racers.AddRacer(gone);

        await Life().ReviewDayAsync(_state, _state.Date.AddDays(OpponentRules.ComeBackAfterDays - 1));

        Assert.Equal(RacerStatus.Departed, gone.Status);
    }

    [Fact]
    public async Task The_player_buys_a_rivals_car_out_of_the_paper()
    {
        var spare = GoodCar("car_a", 250);
        spare.OdometerKM = 1234;
        var seller = Racer("Sal", 100m, RacerStatus.ReadyToRace, GoodCar(), spare);
        var ad = RivalCarAds.Post(_state, seller, spare, 700m, _state.Date);
        Assert.Single(RivalCarAds.Live(_state));
        var service = new CarPurchaseService(new RaceResultProcessorTests.FakeRepository(), new NoParts(), new QuietTime());

        // The price came down since the page was drawn: the ad's price is paid
        ad.AskingPrice = 600m;
        var result = await service.PurchaseFromRivalAsync(_state, ad, new CarDefinition { Id = "car_a", Name = "A", Brand = "Ford" });

        Assert.True(result.Succeeded);
        Assert.Same(spare, Assert.Single(_state.Player.Cars));
        Assert.Equal(1234, spare.OdometerKM);
        Assert.Equal(Player.StartingMoney - 600m, _state.Player.Money);
        Assert.Equal(700m, seller.Money);
        Assert.Empty(_state.NewspaperAds.RivalCars);

        // Sold means gone
        var again = await service.PurchaseFromRivalAsync(_state, ad, new CarDefinition { Id = "car_a", Name = "A", Brand = "Ford" });
        Assert.Equal(PurchaseOutcome.NoLongerAvailable, again.Outcome);
    }

    [Fact]
    public void An_ad_for_the_car_the_rival_now_drives_does_not_stand()
    {
        var spare = GoodCar("car_spare");
        var seller = Racer("Sal", 100m, RacerStatus.ReadyToRace, GoodCar(), spare);
        RivalCarAds.Post(_state, seller, spare, 700m, _state.Date);

        seller.Cars.RemoveAt(0);

        Assert.Empty(RivalCarAds.Live(_state));
        Assert.Equal(1, RivalCarAds.Tidy(_state));
    }
}

public class RivalPartAdsTests
{
    private readonly CatalogParts _parts = new();
    private readonly GameState _state;
    private readonly Opponent _seller;

    public RivalPartAdsTests()
    {
        TestLogging.SilenceLogging();
        _state = GameState.CreateNew("P");
        _seller = new Opponent("Sal", 30, Gender.Male, 92, 50) { Money = 0m };
        _state.Racers.AddRacer(_seller);
    }

    private PartInstance SomePart() =>
        new(_parts.Catalog.Parts.Values.First(p => p.IsScripted && PartPricing.NewPrice(p) > 100).Id) { Wear = 0.8 };

    [Fact]
    public void The_player_buying_a_rivals_part_pays_the_rival()
    {
        var ad = RivalPartAds.Post(_state, _parts.Catalog, _seller, SomePart(), _state.Date, 0.5)!;
        var shop = new PartsShopService(_parts);

        Assert.True(shop.BuyUsed(_state, ad));
        Assert.Equal(ad.AskingPrice, _seller.Money);
        Assert.Equal("Sal", ad.SellerName);
    }

    [Fact]
    public async Task A_rivals_part_nobody_bought_goes_to_a_shop_for_the_trade_in()
    {
        var part = SomePart();
        var ad = RivalPartAds.Post(_state, _parts.Catalog, _seller, part, _state.Date, 0.5)!;
        var shop = new PartsShopService(_parts);

        await shop.RefreshAdsAsync(_state, _state.Date.AddDays(30));

        Assert.DoesNotContain(ad, _state.NewspaperAds.Parts);
        Assert.Equal(PartPricing.TradeIn(_parts.Catalog, part), _seller.Money);
        Assert.True(_seller.Money > 0);
    }

    [Fact]
    public void A_rival_does_not_buy_their_own_part_and_taking_one_pays_its_seller()
    {
        var ad = RivalPartAds.Post(_state, _parts.Catalog, _seller, SomePart(), _state.Date, 0.5)!;
        var buyer = new Opponent("Bea", 30, Gender.Female, 92, 50);

        Assert.Empty(RivalPartAds.OffersFor(_state, _seller));
        Assert.Equal(ad.AdId, Assert.Single(RivalPartAds.OffersFor(_state, buyer)).AdId);

        Assert.True(RivalPartAds.Take(_state, ad.AdId));
        Assert.False(RivalPartAds.Take(_state, ad.AdId));
        Assert.Equal(ad.AskingPrice, _seller.Money);
    }

    [Fact]
    public void An_ad_that_ran_out_is_not_for_sale_to_rivals_while_it_waits_for_the_shops()
    {
        RivalPartAds.Post(_state, _parts.Catalog, _seller, SomePart(), _state.Date.AddDays(-11), 0.5);
        var buyer = new Opponent("Bea", 30, Gender.Female, 92, 50);

        Assert.Empty(RivalPartAds.OffersFor(_state, buyer));
    }
}

/// <summary>The tuner buying out of the paper, on the real parts and the real dyno</summary>
public class UsedTuningTests(ITestOutputHelper output)
{
    private static readonly CatalogParts Parts = new();
    private const string La340 = "chrysler-v8-pack/chrysler-la-340-275-hp";

    private static RatedBuild Build(string idEnd) => Parts.Builds.Runnable.Single(b => b.Build.Id.EndsWith(idEnd, StringComparison.Ordinal));

    [Fact]
    public void A_part_in_the_paper_is_bought_used_when_it_asks_less_and_what_came_off_is_handed_back_loose()
    {
        var catalog = Parts.Catalog;
        var stock = EngineFactory.CreateStock(catalog, Build(La340), 0.8, new Random(1))!;
        var stockIds = stock.Root.SelfAndDescendants().Select(p => p.InstanceId).ToHashSet();

        // A seed whose upgrade is a bolt-on, and the new part it puts on
        foreach (var seed in Enumerable.Range(1, 20))
        {
            var bought = EngineTuner.TuneUp(catalog, Parts.Builds, stock.Root, 1_000_000m, 1.0, new Random(seed), maxUpgrades: 1);
            if (bought?.Upgrades.Single() is not { Group: not EngineTuner.BlockGroup } upgrade) continue;

            var fitted = bought.Engine.SelfAndDescendants()
                .First(p => !stockIds.Contains(p.InstanceId) && catalog.Get(p.DefinitionId) is { } d && PartKinds.GroupOf(d) == upgrade.Group);
            var offer = new UsedPartOffer(Guid.NewGuid(), new PartInstance(fitted.DefinitionId), 1m);

            var used = EngineTuner.TuneUp(catalog, Parts.Builds, stock.Root, 1_000_000m, 1.0, new Random(seed), maxUpgrades: 1, used: [offer])!;
            var usedUpgrade = used.Upgrades.Single();
            output.WriteLine($"#{seed}: {upgrade.Change} ${upgrade.Cost} -> {usedUpgrade.Change} ${usedUpgrade.Cost}");

            Assert.Equal(offer.AdId, usedUpgrade.UsedAdId);
            Assert.True(usedUpgrade.Cost < upgrade.Cost);
            Assert.Contains(used.Engine.SelfAndDescendants(), p => p.InstanceId == offer.Part.InstanceId);

            // What came off: loose, not on the engine any more, and what a shop pays for it
            Assert.NotEmpty(usedUpgrade.Removed!);
            Assert.All(usedUpgrade.Removed!, p =>
            {
                Assert.Empty(p.Children);
                Assert.Equal(0, p.ParentSlot);
                Assert.DoesNotContain(used.Engine.SelfAndDescendants(), q => q.InstanceId == p.InstanceId);
            });
            Assert.Equal(usedUpgrade.TradeIn, usedUpgrade.Removed!.Sum(p => PartPricing.TradeIn(catalog, p)));
            return;
        }

        Assert.Fail("no seed tuned a bolt-on: the test proves nothing");
    }

    [Fact]
    public void A_whole_engine_in_the_paper_can_be_the_swap()
    {
        var catalog = Parts.Catalog;
        var current = Build(La340);
        var stock = EngineFactory.CreateStock(catalog, current, 0.8, new Random(1))!;
        var power = stock.Report.Dyno!.MaxPowerHp;

        var bigger = Parts.Builds.Runnable.FirstOrDefault(b => b.Family == current.Family && b.PowerHp >= power * 1.1 && b.PowerHp <= power * EngineTuner.MaxSwapPower);
        Assert.NotNull(bigger);
        var engine = EngineFactory.CreateStock(catalog, bigger!, 0.9, new Random(2))!.Root;
        engine.ParentSlot = 0;
        var offer = new UsedPartOffer(Guid.NewGuid(), engine, 5m);

        // Five dollars buys nothing new
        var result = EngineTuner.TuneUp(catalog, Parts.Builds, stock.Root, 5m, 1.0, new Random(3), maxUpgrades: 1, used: [offer]);

        Assert.NotNull(result);
        var upgrade = result!.Upgrades.Single();
        output.WriteLine($"{upgrade.Change}: {upgrade.PowerBefore:0} -> {upgrade.PowerAfter:0} hp");
        Assert.Equal(EngineTuner.BlockGroup, upgrade.Group);
        Assert.Equal(offer.AdId, upgrade.UsedAdId);
        Assert.Equal(stock.Root.ParentSlot, result.Engine.ParentSlot);
        Assert.Equal(stock.Root.InstanceId, Assert.Single(upgrade.Removed!).InstanceId);
        Assert.NotEmpty(upgrade.Removed![0].Children);
    }
}

public class NewcomerTests
{
    private sealed class Definitions(params Opponent[] opponents) : IOpponentRepository
    {
        public List<Opponent> LoadAllOpponents() => opponents.Select(o => new Opponent(o.Name, o.Age, o.Gender, o.Skill, o.Aggression)
        {
            DefinitionId = o.DefinitionId, IsKing = o.IsKing, PortraitPath = o.PortraitPath
        }).ToList();

        public Opponent? LoadOpponent(string opponentId) => null;
        public bool DefinitionsFileExists() => true;
    }

    [Fact]
    public void Racers_defined_since_the_save_began_join_it_off_the_street_and_the_King_gets_his_portrait()
    {
        TestLogging.SilenceLogging();
        var state = GameState.CreateNew("P");
        var old = new Opponent("Eddie", 30, Gender.Male, 92, 50) { DefinitionId = "drv_001", Status = RacerStatus.ReadyToRace };
        var king = new Opponent("The King", 41, Gender.Male, 100, 65) { DefinitionId = "king", IsKing = true, Status = RacerStatus.Inactive };
        state.Racers.AddRacer(old);
        state.Racers.AddRacer(king);

        var repo = new Definitions(
            new Opponent("Eddie", 30, Gender.Male, 92, 50) { DefinitionId = "drv_001" },
            new Opponent("Marcus", 43, Gender.Male, 91, 50) { DefinitionId = "drv_031", PortraitPath = "/Assets/Opponents/drv_031.png" },
            new Opponent("The King", 41, Gender.Male, 100, 65) { DefinitionId = "king", IsKing = true, PortraitPath = "/Assets/Opponents/king.png" });
        var init = new OpponentInitializationService(repo, new MemoryCatalog(), new MemoryProfiles());

        init.EnsureNewcomers(state);
        init.EnsureNewcomers(state); // once per game

        var newcomer = Assert.IsType<Opponent>(state.Racers.Inactive["Marcus"]);
        Assert.Equal("drv_031", newcomer.DefinitionId);
        Assert.True(newcomer.Money > 0);
        Assert.Equal(3, state.Racers.All.Count()); // Eddie, the King, Marcus
        Assert.Equal("/Assets/Opponents/king.png", king.PortraitPath);
    }
}
