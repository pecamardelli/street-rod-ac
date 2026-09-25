using LiteDB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Screens.Shared;
using Street_Rod_AC.Services.Market;
using Street_Rod_AC.Services.News;
using Street_Rod_AC.Services.Opponents;
using Street_Rod_AC.Services.Race;
using Street_Rod_AC.Services.Storage;

namespace StreetRodAC.Tests;

/// <summary>Step 10, the world remembers: car history and its price, grudges and the rematch, the paper's race pages</summary>
public class CarHistoryTests
{
    [Fact]
    public void A_car_nobody_knows_anything_about_is_worth_what_it_was()
    {
        Assert.Equal(1m, CarValuation.HistoryFactor(null, 0));
        Assert.Equal(1m, CarValuation.HistoryFactor(new CarHistory(), 50_000));
    }

    [Fact]
    public void Wins_add_to_the_worth_a_pink_slip_taken_more_and_never_past_fifteen_percent()
    {
        Assert.Equal(1.03m, CarValuation.HistoryFactor(new CarHistory { Wins = 3 }, 0));
        Assert.Equal(1.07m, CarValuation.HistoryFactor(new CarHistory { Wins = 3, PinkSlipsWon = 2 }, 0));
        Assert.Equal(1.15m, CarValuation.HistoryFactor(new CarHistory { Wins = 48, PinkSlipsWon = 12 }, 0));
    }

    [Fact]
    public void Many_owners_and_many_miles_take_off_the_worth_never_past_fifteen_percent()
    {
        // Two owners are nothing to speak of; each one more takes two points
        Assert.Equal(1m, CarValuation.HistoryFactor(new CarHistory { EarlierOwners = 2 }, 0));
        Assert.Equal(0.96m, CarValuation.HistoryFactor(new CarHistory { EarlierOwners = 4 }, 0));
        // A point for every 10,000 km past 80,000
        Assert.Equal(0.97m, CarValuation.HistoryFactor(null, 115_000));
        Assert.Equal(0.85m, CarValuation.HistoryFactor(new CarHistory { EarlierOwners = 20 }, 900_000));
        Assert.Equal(1m, CarValuation.HistoryFactor(null, double.NaN));
    }

    [Fact]
    public void A_winning_car_is_worth_more_than_the_same_car_that_never_raced()
    {
        var plain = new Car("car_a");
        var winner = new Car("car_a") { History = new CarHistory { Wins = 5, Races = 6 } };

        Assert.Equal(10000m * CarValuation.ConditionFactor(1), CarValuation.ValueOf(plain, 10000m));
        Assert.True(CarValuation.ValueOf(winner, 10000m) > CarValuation.ValueOf(plain, 10000m));
    }

    [Fact]
    public void A_car_bought_off_a_lot_keeps_its_history_and_the_buyer_is_its_newest_owner()
    {
        var listing = new UsedCarListing
        {
            CarDefinitionId = "car_a",
            History = new CarHistory { EarlierOwners = 2, Wins = 4, Races = 9, Owners = [new CarOwner { Name = "Rex", How = CarAcquisition.Dealer }] }
        };

        var car = CarPurchaseService.CarFrom(listing, new DateTime(1970, 7, 1), "Player");

        Assert.Equal(4, car.History.OwnerCount);
        Assert.Equal("Player", car.History.Owners[^1].Name);
        Assert.Equal(CarAcquisition.Dealer, car.History.Owners[^1].How);
        Assert.Equal(new DateTime(1970, 7, 1), car.History.Owners[^1].Since);
        Assert.Equal(4, car.History.Wins);
        // The listing's own history is not the car's
        Assert.Single(listing.History.Owners);
    }

    [Fact]
    public void A_car_sold_to_a_dealer_goes_on_the_lot_with_its_history()
    {
        TestLogging.SilenceLogging();
        var catalog = new MemoryCatalog();
        var market = new UsedCarMarketService(catalog, new MemoryProfiles());
        var car = new Car("car_a") { History = new CarHistory { Wins = 2, Races = 3, Owners = [new CarOwner { Name = "Player" }] } };

        var listing = market.ListCar(car, 5000m, "industrial_motors", new DateTime(1970, 7, 1));

        Assert.Equal(2, listing.History.Wins);
        Assert.Equal("Player", listing.History.Owners.Single().Name);
        Assert.NotSame(car.History, listing.History);
    }

    [Fact]
    public void The_history_reads_the_same_on_every_screen()
    {
        var history = new CarHistory
        {
            EarlierOwners = 1, Races = 8, Wins = 5, PinkSlipsWon = 2,
            Owners = [new CarOwner { Name = "Johnny", How = CarAcquisition.Dealer }, new CarOwner { Name = "Player", How = CarAcquisition.PinkSlip }]
        };

        Assert.Equal("2 owners before you · won from Johnny on a pink slip · 5 wins in 8 races, 2 cars won on pink slips",
            CarHistoryDisplay.Summary(history, forSale: false));
        Assert.Equal("3 previous owners, the last Player · 5 wins in 8 races, 2 cars won on pink slips",
            CarHistoryDisplay.Summary(history, forSale: true));
        Assert.Equal("First owner · Never raced", CarHistoryDisplay.Summary(new CarHistory { Owners = [new CarOwner { Name = "P" }] }, forSale: false));
    }

    [Fact]
    public void An_older_save_gives_every_car_its_owner_so_the_next_change_of_hands_counts_from_there()
    {
        var state = GameState.CreateNew("Player");
        state.News = null!;
        var mine = new Car("car_a") { History = null! };
        state.Player.Cars.Add(mine);
        var rival = new Opponent("Rex", 30, Gender.Male, 95, 50);
        rival.Cars.Add(new Car("car_b"));
        state.Racers.AddRacer(rival);
        state.UsedCarMarket.Add(new UsedCarListing { History = null! });

        HistoryUpgrade.BringUpToDate(state);
        HistoryUpgrade.BringUpToDate(state);

        Assert.NotNull(state.News);
        Assert.Equal("Player", mine.History.Owners.Single().Name);
        Assert.Equal("Rex", rival.Cars[0].History.Owners.Single().Name);
        Assert.NotNull(state.UsedCarMarket[0].History);
    }

    [Fact]
    public void History_grudges_and_the_news_survive_a_save()
    {
        using var db = new LiteDatabase(new MemoryStream(), SaveMapper.Create());
        var state = GameState.CreateNew("Player");
        var car = new Car("car_a");
        car.History.ChangeHands("Player", new DateTime(1970, 6, 2), CarAcquisition.PinkSlip);
        car.History.RecordRace(won: true, pinkSlip: true);
        state.Player.Cars.Add(car);
        var rival = new Opponent("Rex", 30, Gender.Male, 95, 50) { Grudge = new Grudge { CarInstanceId = car.InstanceId, CarDefinitionId = "car_a", Until = new DateTime(1970, 6, 16) } };
        state.Racers.AddRacer(rival);
        state.News.Add(new NewsArticle { Date = new DateTime(1970, 6, 2), Headline = "H", Body = "B", Weight = 60, AboutPlayer = true });

        var collection = db.GetCollection<GameState>("GameState");
        collection.Upsert(state);
        var loaded = collection.FindById(1);

        var history = loaded.Player.Cars.Single().History;
        Assert.Equal(CarAcquisition.PinkSlip, history.Owners.Single().How);
        Assert.Equal((1, 1, 1), (history.Races, history.Wins, history.PinkSlipsWon));
        Assert.Equal(car.InstanceId, ((Opponent)loaded.Racers.Find("Rex")!).Grudge!.CarInstanceId);
        Assert.Equal("H", loaded.News.Single().Headline);
    }
}

public class GrudgeTests
{
    private static Opponent Rival() => new("Rex", 30, Gender.Male, 95, 50);

    [Fact]
    public void A_rival_who_lost_a_pink_slip_wants_the_car_back_for_two_weeks()
    {
        var rival = Rival();
        var car = new Car("car_b");

        var line = Grudges.Start(rival, car, new DateTime(1970, 6, 2, 15, 0, 0), "Player", "1969 Camaro");

        Assert.True(Grudges.WantsRematch(rival));
        Assert.Equal(car.InstanceId, rival.Grudge!.CarInstanceId);
        Assert.Equal(new DateTime(1970, 6, 16, GameState.DayEndHour, 0, 0), rival.Grudge.Until);
        Assert.Contains("1969 Camaro", line);
    }

    [Fact]
    public void The_rematch_settles_it_and_the_street_hears_when_the_rival_got_even()
    {
        var rival = Rival();
        Grudges.Start(rival, new Car("car_b"), new DateTime(1970, 6, 2), "Player", "car");
        Assert.NotNull(Grudges.Settle(rival, rivalWon: true, "Player"));
        Assert.False(Grudges.WantsRematch(rival));

        Grudges.Start(rival, new Car("car_b"), new DateTime(1970, 6, 2), "Player", "car");
        Assert.Null(Grudges.Settle(rival, rivalWon: false, "Player"));
        Assert.False(Grudges.WantsRematch(rival));
    }

    [Fact]
    public void A_rematch_nobody_came_for_lapses()
    {
        var waiting = Rival();
        var gaveUp = new Opponent("Sal", 40, Gender.Female, 92, 40);
        Grudges.Start(waiting, new Car("car_b"), new DateTime(1970, 6, 10), "Player", "car");
        Grudges.Start(gaveUp, new Car("car_c"), new DateTime(1970, 6, 1), "Player", "car");
        var talk = new List<string>();

        Grudges.Lapse([waiting, gaveUp], new DateTime(1970, 6, 16, 8, 0, 0), id => id, talk);

        Assert.True(Grudges.WantsRematch(waiting));
        Assert.False(Grudges.WantsRematch(gaveUp));
        Assert.Equal("Sal has stopped talking about getting her car_c back.", talk.Single());
    }

    [Fact]
    public void A_rival_who_wants_a_rematch_takes_any_pink_slip_race_but_not_every_cash_race()
    {
        TestLogging.SilenceLogging();
        var catalog = new MemoryCatalog();
        var profiles = new MemoryProfiles();
        foreach (var (id, price) in new[] { ("cheap", 1000m), ("dear", 10000m) })
        {
            catalog.UpsertCar(new CarDefinition { Id = id, Name = id, Brand = "Ford" });
            profiles.UpsertProfile(new CarProfile { CarDefinitionId = id, BasePrice = price });
        }
        var service = new OpponentChallengeService(catalog, profiles);
        var player = GameState.CreateNew("P").Player;
        var rival = Rival();
        Grudges.Start(rival, new Car("dear"), new DateTime(1970, 6, 2), "P", "car");

        // Without the grudge a cheap car is never staked against a dear one
        for (var i = 0; i < 20; i++)
            Assert.True(service.EvaluateChallenge(rival, player, new Car("cheap"), new Car("dear"), isPinkSlip: true).Accepted);

        var broke = Rival();
        broke.Grudge = rival.Grudge;
        broke.Money = 0;
        Assert.False(service.EvaluateChallenge(broke, player, new Car("cheap"), new Car("dear"), isPinkSlip: false, cashWager: 100m).Accepted);
    }
}

[Collection(RaceSessionTests.Name)]
public sealed class WorldRemembersRaceTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly RaceResultProcessorTests.FakeRepository _repository = new();

    public void Dispose() => _temp.Dispose();

    private RaceResultProcessor Processor() =>
        new(_repository, new RaceSessionRepository(new SaveDatabase(_temp.Combine("unused-saves"))), new RaceFakes.FakeCareer(), new RaceFakes.FakeEvents());

    private static RaceResultJson Result(int playerPosition)
    {
        var json = RaceResultValidatorTests.Fixture("1.1", RaceFakes.PlayerName, RaceFakes.OpponentName);
        json["participants"]![0]!["performance"]!["final_position"] = playerPosition;
        json["participants"]![1]!["performance"]!["final_position"] = 3 - playerPosition;
        return JsonConvert.DeserializeObject<RaceResultJson>(json.ToString())!;
    }

    [Fact]
    public void A_pink_slip_won_moves_the_car_with_its_history_and_the_rival_wants_it_back()
    {
        var world = RaceFakes.World(wager: 0m, pinkSlip: true);
        world.RivalCar.History.ChangeHands(RaceFakes.OpponentName, new DateTime(1970, 1, 1), CarAcquisition.Unknown);

        Processor().ProcessRaceResult(Result(playerPosition: 1), world.Context, world.State);

        var taken = world.State.Player.Cars.Single(c => c.InstanceId == world.RivalCar.InstanceId);
        Assert.Equal((1, 0), (taken.History.Races, taken.History.Wins));
        Assert.Equal(RaceFakes.PlayerName, taken.History.Owners[^1].Name);
        Assert.Equal(CarAcquisition.PinkSlip, taken.History.Owners[^1].How);
        Assert.Equal((1, 1, 1), (world.PlayerCar.History.Races, world.PlayerCar.History.Wins, world.PlayerCar.History.PinkSlipsWon));

        Assert.Equal(taken.InstanceId, world.Rival.Grudge!.CarInstanceId);
        Assert.Contains(world.State.StreetTalk, t => t.Text.Contains("back from"));
        var article = world.State.News.Single();
        Assert.True(article.AboutPlayer);
        Assert.Contains("isn't over", article.Body);

        // The race as history keeps it
        var session = _repository.Recorded.Single();
        Assert.Equal(world.State.Date, session.GameDate);
        Assert.Equal(world.RivalCar.InstanceId, session.OpponentCarInstanceId);
        Assert.Equal(nameof(RaceType.DragRace), session.RaceType);
    }

    [Fact]
    public void The_rival_who_gets_even_in_the_rematch_lets_it_go_and_the_street_hears()
    {
        var world = RaceFakes.World(wager: 0m, pinkSlip: true);
        Grudges.Start(world.Rival, new Car("car_c"), world.State.Date.AddDays(-3), RaceFakes.PlayerName, "car");

        Processor().ProcessRaceResult(Result(playerPosition: 2), world.Context, world.State);

        Assert.Null(world.Rival.Grudge);
        Assert.Contains(world.State.StreetTalk, t => t.Text.Contains("got even"));
        Assert.Contains(world.Rival.Cars, c => c.InstanceId == world.PlayerCar.InstanceId);
        Assert.Equal(RaceFakes.OpponentName, world.PlayerCar.History.Owners[^1].Name);
        var article = world.State.News.Single();
        Assert.Equal(70, article.Weight);
        Assert.Contains("rematch", article.Body);
    }

    [Fact]
    public void An_ordinary_cash_race_makes_no_news()
    {
        var world = RaceFakes.World(wager: 100m);
        world.State.Player.Stats.Wins = 3; // not the first win

        Processor().ProcessRaceResult(Result(playerPosition: 1), world.Context, world.State);

        Assert.Empty(world.State.News);
        Assert.Equal(1, world.RivalCar.History.Races);
    }

    [Fact]
    public void A_race_that_fails_to_save_leaves_no_talk_or_news_behind()
    {
        var world = RaceFakes.World(wager: 0m, pinkSlip: true);
        var repository = new RaceResultProcessorTests.FakeRepository { Throw = new InvalidOperationException("disk full") };
        var processor = new RaceResultProcessor(repository, new RaceSessionRepository(new SaveDatabase(_temp.Combine("unused"))),
            new RaceFakes.FakeCareer(), new RaceFakes.FakeEvents());

        Assert.ThrowsAny<Exception>(() => processor.ProcessRaceResult(Result(playerPosition: 1), world.Context, world.State));

        Assert.Empty(world.State.News);
        Assert.Empty(world.State.StreetTalk);
        Assert.Null(((Opponent)world.State.Racers.Find(RaceFakes.OpponentName)!).Grudge);
    }
}

public class NewsWriterTests
{
    private static PlayerRaceFacts Facts => new()
    {
        Date = new DateTime(1970, 6, 5), PlayerName = "Pablo", RivalName = "Rex", PlayerCar = "1969 Chevrolet Camaro",
        RivalCar = "1970 Ford Mustang", IsDrag = true, Decided = true, PlayerReputation = 30, RivalReputation = 35
    };

    [Fact]
    public void There_is_a_story_in_pink_slips_the_King_the_police_and_upsets_and_none_in_an_ordinary_race()
    {
        var random = new Random(1);
        Assert.Null(NewsWriter.PlayerRace(Facts with { PlayerWon = true, CashWager = 100m }, random));

        Assert.Equal(60, NewsWriter.PlayerRace(Facts with { PlayerWon = true, PinkSlip = true }, random)!.Weight);
        Assert.Equal(100, NewsWriter.PlayerRace(Facts with { PlayerWon = true, PinkSlip = true, RivalIsKing = true }, random)!.Weight);
        Assert.Equal(50, NewsWriter.PlayerRace(Facts with { Decided = false, PlayerEscaped = true }, random)!.Weight);
        Assert.Equal(35, NewsWriter.PlayerRace(Facts with { PlayerWon = true, RivalReputation = 60 }, random)!.Weight);
        Assert.Equal(30, NewsWriter.PlayerRace(Facts with { PlayerWon = false, CashWager = 2500m }, random)!.Weight);
        Assert.Equal(25, NewsWriter.PlayerRace(Facts with { PlayerWon = true, FirstWin = true }, random)!.Weight);
        Assert.Null(NewsWriter.PlayerRace(Facts with { Decided = false }, random));
    }

    [Fact]
    public void Only_pink_slips_and_wrecks_of_the_rivals_make_the_paper()
    {
        var random = new Random(1);
        var date = new DateTime(1970, 6, 5);
        Assert.Null(NewsWriter.RivalRace(date, "A", "B", "1970 Ford Mustang", true, pinkSlip: false, loserCrashed: false, 0, random));
        Assert.Contains("7 wins", NewsWriter.RivalRace(date, "A", "B", "1970 Ford Mustang", true, pinkSlip: true, loserCrashed: false, 7, random)!.Body);
        Assert.Equal(15, NewsWriter.RivalRace(date, "A", "B", "1970 Ford Mustang", false, pinkSlip: false, loserCrashed: true, 0, random)!.Weight);
    }

    [Fact]
    public void The_front_page_is_the_last_three_days_newest_first_the_weightiest_first_within_a_day()
    {
        var today = new DateTime(1970, 6, 10, 14, 0, 0);
        NewsArticle A(int daysAgo, int weight, string h) => new() { Date = today.AddDays(-daysAgo), Weight = weight, Headline = h };
        var news = new List<NewsArticle> { A(3, 100, "too old"), A(1, 60, "yesterday"), A(0, 15, "small today"), A(0, 50, "big today"), A(2, 30, "two days") };

        var page = NewsWriter.FrontPage(news, today);

        Assert.Equal(new[] { "big today", "small today", "yesterday", "two days" }, page.Select(a => a.Headline));
    }

    [Fact]
    public void The_paper_keeps_two_weeks_of_pieces_at_most()
    {
        var state = GameState.CreateNew("P");
        var today = new DateTime(1970, 7, 1);
        state.News.Add(new NewsArticle { Date = today.AddDays(-20), Headline = "old" });

        NewsWriter.Add(state, today, Enumerable.Range(0, 50).Select(i => new NewsArticle { Date = today, Headline = $"n{i}" }));

        Assert.Equal(NewsWriter.MaxKept, state.News.Count);
        Assert.DoesNotContain(state.News, a => a.Headline == "old");
        Assert.Equal("n49", state.News[^1].Headline);
    }
}
