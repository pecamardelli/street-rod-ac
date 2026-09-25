using Street_Rod_AC.Models.Career.Events;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Screens.Diner;
using Street_Rod_AC.Services.Market;
using Street_Rod_AC.Services.Opponents;
using Street_Rod_AC.Services.Storage;
using Street_Rod_AC.Services.Time;

namespace StreetRodAC.Tests;

public class GameRulesTests
{
    [Fact]
    public void Normal_changes_nothing()
    {
        var rules = GameRules.For(Difficulty.Normal);
        Assert.Equal(1.0, rules.CarPriceMultiplier);
        Assert.Equal(1.0, rules.PartPriceMultiplier);
        Assert.Equal(1.0, rules.RacePrizeMultiplier);
        Assert.Equal(1.0, rules.CarWearMultiplier);
        Assert.Equal(0, rules.OpponentSkillModifier);
        Assert.Equal(1.0, rules.PinkSlipFactor);
        Assert.Equal(100, rules.RaceDamagePercent);
    }

    [Fact]
    public void Easy_is_kinder_than_hard_on_every_count()
    {
        var easy = GameRules.For(Difficulty.Easy);
        var hard = GameRules.For(Difficulty.Hard);
        Assert.True(easy.CarPriceMultiplier < 1 && hard.CarPriceMultiplier > 1);
        Assert.True(easy.PartPriceMultiplier < 1 && hard.PartPriceMultiplier > 1);
        Assert.True(easy.RacePrizeMultiplier > 1 && hard.RacePrizeMultiplier < 1);
        Assert.True(easy.OpponentSkillModifier < 0 && hard.OpponentSkillModifier > 0);
        Assert.True(easy.PinkSlipFactor < 1 && hard.PinkSlipFactor > 1);
        Assert.True(easy.RaceDamagePercent < 100);
        Assert.Equal(100, hard.RaceDamagePercent); // AC tops out at 100: a hard game wears the parts faster instead
    }

    [Theory]
    [InlineData(double.NaN, 1.0)]
    [InlineData(-2.0, 1.0)]
    [InlineData(0.0, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    [InlineData(1e9, 100.0)]
    [InlineData(0.85, 0.85)]
    public void A_multiplier_from_a_hand_edited_save_is_made_sane(double multiplier, double expected) =>
        Assert.Equal(expected, GameRules.Sane(multiplier));

    [Fact]
    public void A_copy_is_its_own()
    {
        var rules = GameRules.For(Difficulty.Hard);
        var copy = rules.Copy();
        copy.CarPriceMultiplier = 3;
        Assert.Equal(1.15, rules.CarPriceMultiplier);
    }

    [Fact]
    public void Event_prizes_scale_on_a_copy_to_the_nearest_five()
    {
        var reward = new EventReward(500m, 10);
        var scaled = reward.ScaledBy(0.85);
        Assert.Equal(425m, scaled.Cash);
        Assert.Equal(10, scaled.Reputation);
        Assert.Equal(500m, reward.Cash);
        Assert.Equal(625m, reward.ScaledBy(1.25).Cash);
        Assert.Equal(500m, reward.ScaledBy(double.NaN).Cash);
    }

    [Fact]
    public void The_difficulty_moves_the_rivals_AI_within_what_AC_takes()
    {
        var opponent = new Opponent("Vic", 30, Gender.Male, 95, 50);
        Assert.Equal(95, OpponentAIAdapter.ToAssettoCorsaAI(opponent).AILevel);

        var easy = OpponentAIAdapter.ToAssettoCorsaAI(opponent, GameRules.For(Difficulty.Easy));
        Assert.Equal(92, easy.AILevel);
        Assert.Equal(35, easy.AIAggression);

        var hard = OpponentAIAdapter.ToAssettoCorsaAI(new Opponent("Ace", 30, Gender.Male, 99, 95), GameRules.For(Difficulty.Hard));
        Assert.Equal(Opponent.MaxSkill, hard.AILevel);
        Assert.Equal(100, hard.AIAggression);

        var floor = OpponentAIAdapter.ToAssettoCorsaAI(new Opponent("Slow", 30, Gender.Male, 90, 0), new GameRules { OpponentSkillModifier = -20 });
        Assert.Equal(OpponentAIAdapter.EasiestSkill, floor.AILevel);
    }

    [Fact]
    public void Repairs_cost_what_the_difficulty_says()
    {
        var car = new Car("car_a") { BodyDamageKmh = new double[] { 30, 0, 0, 0 } };
        var normal = Assert.Single(RepairShop.Jobs(car, catalog: null)).Cost;
        var hard = Assert.Single(RepairShop.Jobs(car, catalog: null, priceMultiplier: 1.15)).Cost;
        Assert.Equal(RepairShop.Labour + 30 * RepairShop.BodyCostPerKmh, normal);
        Assert.True(hard > normal);
    }

    [Fact]
    public void A_save_keeps_its_rules_and_an_older_one_plays_on_normal()
    {
        using var temp = new TempDir();
        var database = new SaveDatabase(temp.Combine("Saves"));
        using var repository = new GameStateRepository(null, database);

        repository.CreateNew("Hard", "Hard", GameRules.For(Difficulty.Hard));
        var loaded = repository.Load("Hard")!;
        Assert.Equal(Difficulty.Hard, loaded.Rules.Difficulty);
        Assert.Equal(1.15, loaded.Rules.CarPriceMultiplier);

        // A save from before the rules: the document has no Rules at all
        var old = repository.CreateNew("Old", "Old");
        database.InTransaction("Old", db =>
        {
            var collection = db.GetCollection("gamestate");
            var doc = collection.FindById(1);
            doc.Remove("Rules");
            doc["NewspaperAds"].AsDocument.Remove("PlayerCars");
            collection.Update(doc);
        });
        var reloaded = repository.Load("Old")!;
        Assert.Equal(Difficulty.Normal, reloaded.Rules.Difficulty);
        Assert.NotNull(reloaded.NewspaperAds.PlayerCars);
    }
}

public class StakesTests
{
    private static readonly DateTime Afternoon = new(1970, 6, 1, 15, 0, 0);
    private static readonly DateTime Night = new(1970, 6, 1, 21, 0, 0);

    [Theory]
    [InlineData(50, 1.0)]
    [InlineData(20, 1.0)]
    [InlineData(75, 2.0)]
    [InlineData(100, 3.0)]
    [InlineData(250, 3.0)]
    public void A_rival_with_a_name_plays_for_more(int reputation, double factor) =>
        Assert.Equal((decimal)factor, MatchupCalculator.StakesFactor(reputation, Afternoon));

    [Fact]
    public void Night_doubles_the_stakes()
    {
        Assert.Equal(2m, MatchupCalculator.StakesFactor(50, Night));
        Assert.Equal(6m, MatchupCalculator.StakesFactor(100, Night));
        Assert.Equal(1m, MatchupCalculator.StakesFactor(50, null));
    }

    [Fact]
    public void The_house_limit_grows_but_the_poorer_racer_still_caps_it()
    {
        Assert.Equal((10m, 100m), MatchupCalculator.WagerLimits(RaceType.DragRace, 10000, 10000, 50, Afternoon));
        Assert.Equal((10m, 200m), MatchupCalculator.WagerLimits(RaceType.DragRace, 10000, 10000, 50, Night));
        Assert.Equal((25m, 1500m), MatchupCalculator.WagerLimits(RaceType.Circuit, 10000, 10000, 100, Night));
        Assert.Equal((10m, 300m), MatchupCalculator.WagerLimits(RaceType.DragRace, 10000, 300, 100, Night));
    }
}

public class CarSaleServiceTests
{
    private const decimal Worth = 2000m;

    private sealed class FakeTime : IGameTimeService
    {
        public List<GameAction> Spent { get; } = [];
        public int DayStartHour => GameState.DayStartHour;
        public int DayEndHour => GameState.DayEndHour;

        public Task<TimeSpendResult> SpendTimeAsync(GameState gameState, GameAction action)
        {
            Spent.Add(action);
            gameState.Date = gameState.Date.AddMinutes(action.GetTimeInMinutes());
            return Task.FromResult(new TimeSpendResult());
        }

        public Task<TimeSpendResult> SpendTimeAsync(GameState gameState, int minutes) => Task.FromResult(new TimeSpendResult());
        public int GetRemainingMinutesToday(GameState gameState) => 600;
        public double GetRemainingHoursToday(GameState gameState) => 10;
        public string GetFormattedTime(GameState gameState) => string.Empty;
        public string GetFormattedDate(GameState gameState) => string.Empty;
        public bool HasTimeFor(GameState gameState, GameAction action) => true;
        public Task<TimeSpendResult> EndDayAsync(GameState gameState) => Task.FromResult(new TimeSpendResult());
    }

    private readonly FakeMarket _market = new(Worth, [new() { Id = "industrial_motors", Name = "Industrial Motors" }]);
    private readonly FakeTime _time = new();
    private readonly CarSaleService _service;
    private readonly GameState _state;
    private readonly Car _car;
    private readonly Car _other;

    public CarSaleServiceTests()
    {
        TestLogging.SilenceLogging();
        _service = new CarSaleService(_market, _time, new RaceResultProcessorTests.FakeRepository());
        _state = GameState.CreateNew("P");
        _state.Player.Money = 100m;
        _car = new Car("car_a") { InstanceId = Guid.NewGuid() };
        _other = new Car("car_b") { InstanceId = Guid.NewGuid() };
        _state.Player.Cars.Add(_car);
        _state.Player.Cars.Add(_other);
        _state.Player.SelectedCarInstanceId = _car.InstanceId;
    }

    [Fact]
    public async Task A_dealer_pays_sixty_percent_and_puts_the_car_on_its_lot_at_its_worth()
    {
        _state.Rules = GameRules.For(Difficulty.Hard);

        var result = await _service.SellToDealerAsync(_state, _car);

        Assert.True(result.Succeeded);
        Assert.Equal(100m + 1200m, _state.Player.Money);
        Assert.DoesNotContain(_car, _state.Player.Cars);
        Assert.Equal(_other.InstanceId, _state.Player.SelectedCarInstanceId);
        Assert.Equal(1, _state.Player.Stats.CarsSold);
        var listed = Assert.Single(_market.Listed);
        Assert.Equal(2300m, listed.Price); // worth, marked up like every car on the lot in a hard game
        Assert.Single(_state.UsedCarMarket);
        Assert.Equal(new[] { GameAction.SellCar }, _time.Spent);
    }

    [Fact]
    public async Task A_totaled_car_goes_for_scrap_and_nowhere_else()
    {
        _car.BodyDamageKmh = new double[] { 0, 0, 205, 205 };

        var quote = _service.QuoteDealer(_state, _car);
        Assert.True(quote.IsScrap);
        Assert.InRange(quote.Amount, 2000m * 0.12m - 10, 2000m * 0.18m + 10);
        Assert.Equal(quote, _service.QuoteDealer(_state, _car)); // the same price every time it is asked

        Assert.Equal(SaleOutcome.Refused, (await _service.PlaceAdAsync(_state, _car, 1000m)).Outcome);

        Assert.True((await _service.SellToDealerAsync(_state, _car)).Succeeded);
        Assert.Empty(_market.Listed);
        Assert.Equal(100m + quote.Amount, _state.Player.Money);
    }

    [Fact]
    public async Task A_car_out_racing_or_not_the_players_is_not_sold()
    {
        _state.PendingRace = new RaceContext { PlayerCarInstanceId = _car.InstanceId };
        Assert.Equal(SaleOutcome.Racing, (await _service.SellToDealerAsync(_state, _car)).Outcome);
        Assert.Equal(SaleOutcome.NotOwned, (await _service.SellToDealerAsync(_state, new Car("x"))).Outcome);
        Assert.Equal(100m, _state.Player.Money);
    }

    [Fact]
    public async Task An_ad_costs_its_fee_and_asks_a_sensible_price()
    {
        Assert.Equal(SaleOutcome.Refused, (await _service.PlaceAdAsync(_state, _car, 50m)).Outcome);
        Assert.Equal(SaleOutcome.Refused, (await _service.PlaceAdAsync(_state, _car, Worth * 4)).Outcome);

        var result = await _service.PlaceAdAsync(_state, _car, 2400m);
        Assert.True(result.Succeeded);
        Assert.Equal(100m - CarSaleService.AdFee, _state.Player.Money);
        var ad = _service.AdFor(_state, _car)!;
        Assert.Equal(2400m, ad.AskingPrice);
        Assert.Contains(_car, _state.Player.Cars); // the car stays until somebody buys it

        Assert.Equal(SaleOutcome.Refused, (await _service.PlaceAdAsync(_state, _car, 2400m)).Outcome);
    }

    [Fact]
    public async Task A_buyer_calls_and_taking_the_offer_sells_the_car()
    {
        await _service.PlaceAdAsync(_state, _car, 1500m);
        var ad = _service.AdFor(_state, _car)!;

        // Asked under its worth: somebody calls within a few days, and pays the asking price
        var random = new Random(1);
        for (var day = 1; day <= 30 && ad.Offer == null; day++) _service.ReviewAds(_state, _state.Date.AddDays(day), random);
        Assert.NotNull(ad.Offer);
        Assert.Equal(1500m, ad.Offer!.Amount);

        var result = await _service.AcceptOfferAsync(_state, ad);
        Assert.True(result.Succeeded);
        Assert.Equal(100m - CarSaleService.AdFee + 1500m, _state.Player.Money);
        Assert.DoesNotContain(_car, _state.Player.Cars);
        Assert.Empty(_state.NewspaperAds.PlayerCars);
        Assert.Empty(_market.Listed); // a private buyer drives it away
    }

    [Fact]
    public async Task A_rival_who_wants_the_car_answers_the_ad_and_races_it_afterwards()
    {
        _car.PowerHp = 300;
        var rival = new Opponent("Vera", 40, Gender.Female, 92, 40) { Money = 5000m };
        rival.Cars.Add(new Car("car_old") { PowerHp = 150 });
        _state.Racers.AddRacer(rival);
        var content = new Opponent("Hal", 40, Gender.Male, 92, 40) { Money = 5000m };
        content.Cars.Add(new Car("car_fast") { PowerHp = 400 }); // has a faster car: not interested
        _state.Racers.AddRacer(content);

        await _service.PlaceAdAsync(_state, _car, 1500m);
        var ad = _service.AdFor(_state, _car)!;

        var random = new Random(1);
        for (var day = 1; day <= 60 && ad.Offer?.RivalName == null; day++)
        {
            ad.Offer = null;
            _service.ReviewAds(_state, _state.Date, random);
        }

        Assert.Equal("Vera", ad.Offer!.RivalName);
        Assert.Equal("Vera", ad.Offer.BuyerName);

        var result = await _service.AcceptOfferAsync(_state, ad);
        Assert.True(result.Succeeded);
        Assert.Contains(_car, rival.Cars);
        Assert.Equal(5000m - 1500m, rival.Money);
        Assert.DoesNotContain(_car, _state.Player.Cars);
        Assert.Contains(_state.StreetTalk, t => t.Text.StartsWith("Vera bought"));
    }

    [Fact]
    public async Task A_rival_who_spent_the_money_meanwhile_cannot_buy()
    {
        var rival = new Opponent("Vera", 40, Gender.Female, 92, 40) { Money = 100m };
        _state.Racers.AddRacer(rival);
        await _service.PlaceAdAsync(_state, _car, 1500m);
        var ad = _service.AdFor(_state, _car)!;
        ad.Offer = new CarOffer { BuyerName = "Vera", RivalName = "Vera", Amount = 1500m, Expires = _state.Date.AddDays(1) };

        var result = await _service.AcceptOfferAsync(_state, ad);

        Assert.Equal(SaleOutcome.NoOffer, result.Outcome);
        Assert.Contains(_car, _state.Player.Cars);
        Assert.Null(ad.Offer);
    }

    [Fact]
    public void Old_ads_and_ads_of_cars_that_are_gone_leave_the_paper_and_an_old_offer_lapses()
    {
        var gone = new CarSaleAd { CarInstanceId = Guid.NewGuid(), AskingPrice = 1000m, PostedDate = _state.Date };
        var old = new CarSaleAd { CarInstanceId = _car.InstanceId, AskingPrice = 1000m, PostedDate = _state.Date.AddDays(-CarSaleService.AdDays - 1) };
        var lapsed = new CarSaleAd
        {
            CarInstanceId = _other.InstanceId, AskingPrice = Worth * 2, PostedDate = _state.Date,
            Offer = new CarOffer { BuyerName = "Earl", Amount = 3000m, Expires = _state.Date.AddHours(-1) }
        };
        _state.NewspaperAds.PlayerCars.AddRange([gone, old, lapsed]);

        _service.ReviewAds(_state, _state.Date, new Random(3));

        Assert.Equal(new[] { lapsed }, _state.NewspaperAds.PlayerCars);
        Assert.Null(lapsed.Offer); // asked at twice its worth, nobody calls
    }

    [Theory]
    [InlineData(1000, 2000, 0.6)]
    [InlineData(1800, 2000, 0.6)]
    [InlineData(2000, 2000, 0.48)]
    [InlineData(2400, 2000, 0.24)]
    [InlineData(2800, 2000, 0.0)]
    public void Fewer_buyers_call_the_more_is_asked(int asking, int value, double chance) =>
        Assert.Equal(chance, CarSaleService.OfferChance(asking, value), 6);

    [Theory]
    [InlineData(1500, 2000, 0.0, 1500)]
    [InlineData(2400, 2000, 1.0, 2400)]
    [InlineData(2400, 2000, 0.0, 2040)]
    [InlineData(3000, 2800, 0.0, 2800)]
    public void Buyers_haggle_over_what_the_car_is_worth_but_not_below_it(int asking, int value, double roll, int offer) =>
        Assert.Equal((decimal)offer, CarSaleService.OfferAmount(asking, value, roll));

    [Fact]
    public void The_scrapyard_pays_fifteen_percent_give_or_take_a_fifth()
    {
        for (var i = 0; i < 200; i++)
            Assert.InRange(CarSaleService.ScrapFactor(Guid.NewGuid()), 0.12 - 1e-9, 0.18 + 1e-9);
    }
}
