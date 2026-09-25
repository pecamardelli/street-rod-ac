using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Services.Configuration;
using Street_Rod_AC.Services.Configuration.Models;
using Street_Rod_AC.Services.Police;
using Street_Rod_AC.Services.Race;
using Street_Rod_AC.Services.Storage;

namespace StreetRodAC.Tests;

/// <summary>The police: the risk of a patrol, the police car, race.ini, and what a chase does to the career</summary>
[Collection(RaceSessionTests.Name)]
public sealed class PoliceTests : IDisposable
{
    private readonly TempDir _temp = new();

    public PoliceTests() => TestLogging.SilenceLogging();

    public void Dispose() => _temp.Dispose();

    private static readonly DateTime Afternoon = new(1970, 6, 3, 15, 0, 0);
    private static readonly DateTime Night = new(1970, 6, 3, 21, 0, 0);

    // ----- the rules -----

    [Fact]
    public void Night_racing_draws_the_police_far_more_than_racing_by_day()
    {
        Assert.Equal(PoliceRules.DayChance, PoliceRules.Chance(Afternoon, 0, false, 0m), 6);
        Assert.Equal(PoliceRules.NightChance, PoliceRules.Chance(Night, 0, false, 0m), 6);
        Assert.True(PoliceRules.Chance(Afternoon, 100, false, 0m) > PoliceRules.Chance(Afternoon, 0, false, 0m));
        Assert.True(PoliceRules.Chance(Night, 50, true, 0m) > PoliceRules.Chance(Night, 50, false, 0m));
        Assert.True(PoliceRules.Chance(Night, 50, false, PoliceRules.BigWager) > PoliceRules.Chance(Night, 50, false, 10m));
        Assert.Equal(PoliceRules.MaxChance, PoliceRules.Chance(Night, 100, true, 1_000_000m), 6);
    }

    [Fact]
    public void The_police_send_one_car_for_each_racer()
    {
        Assert.Equal(2, PoliceCars.Patrol(Monaco, true, Afternoon, 0, false, 0m, "24", ["car_a", "car_b"], new SureRandom())?.Count);
        Assert.Equal(2, PoliceCars.Patrol(Monaco, true, Night, 100, true, 5000m, "24", ["car_a", "car_b"], new SureRandom())?.Count);
        Assert.Equal("Low", PoliceRules.RiskLabel(0.08));
        Assert.Equal("Moderate", PoliceRules.RiskLabel(0.2));
        Assert.Equal("High", PoliceRules.RiskLabel(0.4));
    }

    [Fact]
    public void Each_bust_costs_more_and_keeps_the_car_longer_up_to_a_limit()
    {
        Assert.Equal(750m, PoliceRules.Fine(0));
        Assert.Equal(1250m, PoliceRules.Fine(1));
        Assert.Equal(PoliceRules.MaxFine, PoliceRules.Fine(50));
        Assert.Equal(2, PoliceRules.ImpoundDays(0));
        Assert.Equal(PoliceRules.MaxImpoundDays, PoliceRules.ImpoundDays(50));

        var car = new Car("car_a");
        PoliceRules.Impound(car, Night, earlierBusts: 1);
        Assert.True(car.IsImpounded);
        Assert.Equal(new DateTime(1970, 6, 6, 8, 0, 0), car.ImpoundedUntil);
        Assert.Equal(300m, car.ImpoundFee);
        Assert.False(PoliceRules.CanCollect(car, new DateTime(1970, 6, 6, 7, 59, 0)));
        Assert.True(PoliceRules.CanCollect(car, new DateTime(1970, 6, 6, 8, 0, 0)));
        Assert.Contains("it's in the police impound", CarCondition.WhyCannotRace(car, null));

        PoliceRules.Release(car);
        Assert.False(car.IsImpounded);
        Assert.Equal(0m, car.ImpoundFee);
        Assert.Empty(CarCondition.WhyCannotRace(new Car("car_b") { EngineHealth = 1, TransmissionHealth = 1 }, null));
    }

    // ----- the police car -----

    /// <summary>A car folder with its skins; a skin named in <paramref name="police"/> carries the police marker</summary>
    private string CarFolder(string id, string[] skins, params string[] police)
    {
        foreach (var skin in skins)
        {
            var marker = police.Contains(skin) ? $", \"{PoliceCars.SkinMarker}\": true" : string.Empty;
            _temp.File(Path.Combine("cars", id, "skins", skin, "ui_skin.json"), $"{{ \"skinname\": \"{skin}\"{marker} }}");
        }
        return _temp.Combine("cars", id);
    }

    [Fact]
    public void The_police_car_is_the_one_with_marked_liveries_and_a_monaco_comes_first()
    {
        CarFolder("chevrolet_impala_1962", ["red", "blue"]);
        var galaxie = CarFolder("ford_galaxie_500", ["white", "black_white"], "black_white");
        var monaco = CarFolder("dodge_monaco_police", ["chp", "cpd", "x9_black"], "chp", "cpd", "x9_black");

        var found = PoliceCars.Find(_temp.Combine("cars"));

        Assert.NotNull(found);
        Assert.Equal("dodge_monaco_police", found!.CarId);
        Assert.Equal(["chp", "cpd", "x9_black"], found.Skins);
        // A car with nothing but police liveries is the police's; one with a police skin among others is still a car
        Assert.True(PoliceCars.IsPoliceCar(monaco));
        Assert.False(PoliceCars.IsPoliceCar(galaxie));
        Assert.True(PoliceCars.IsPoliceSkin(Path.Combine(galaxie, "skins", "black_white")));
        Assert.False(PoliceCars.IsPoliceSkin(Path.Combine(galaxie, "skins", "white")));
    }

    [Fact]
    public void Without_a_marked_livery_there_is_no_police_car_even_one_called_police()
    {
        CarFolder("chevrolet_impala_1962", ["red"]);
        CarFolder("ford_police_interceptor", ["black"]);

        Assert.Null(PoliceCars.Find(_temp.Combine("cars")));
        Assert.Null(PoliceCars.Patrol(null, true, Night, 100, true, 5000m, "24", ["car_a", "car_b"], new Random(1)));
    }

    private static readonly PoliceCar Monaco = new("dodge_monaco_74", ["police_a", "police_b"]);

    [Fact]
    public void A_patrol_is_rolled_for_road_races_only_and_never_in_a_racers_own_model()
    {
        var sure = new Random(1);
        Assert.Null(PoliceCars.Patrol(Monaco, isRoadRace: false, Night, 100, true, 5000m, "24", ["car_a", "car_b"], sure));
        Assert.Null(PoliceCars.Patrol(Monaco, true, Night, 100, true, 5000m, "24", ["DODGE_MONACO_74", "car_b"], sure));
        // Two pit boxes are the two racers': the police need one more for each of them
        Assert.Null(PoliceCars.Patrol(Monaco, true, Night, 100, true, 5000m, "2", ["car_a", "car_b"], sure));
        Assert.Null(PoliceCars.Patrol(Monaco, true, Night, 100, true, 5000m, "3", ["car_a", "car_b"], new SureRandom()));
        Assert.Equal(2, PoliceCars.Patrol(Monaco, true, Night, 100, true, 5000m, "4", ["car_a", "car_b"], new SureRandom())?.Count);
    }

    [Fact]
    public void A_patrol_that_comes_sends_each_car_in_a_livery_and_shows_up_part_way_round()
    {
        Assert.Null(PoliceCars.Roll(0, 2, Monaco, new SureRandom()));
        Assert.Null(PoliceCars.Roll(0.5, 2, Monaco, new NeverRandom()));

        var police = PoliceCars.Roll(0.5, 3, Monaco, new SureRandom());

        Assert.NotNull(police);
        Assert.Equal("dodge_monaco_74", police!.CarId);
        Assert.Equal(3, police.Count);
        Assert.Equal(["police_a", "police_b", "police_a"], police.Skins);
        Assert.InRange(police.SpotShare, 0.25, 0.6);
        // A roll of 0 is under the trap share: speed traps
        Assert.True(police.Traps);
    }

    [Fact]
    public void Two_patrols_in_three_are_speed_traps()
    {
        var random = new Random(7);
        var rolls = Enumerable.Range(0, 3000).Select(_ => PoliceCars.Roll(1, 2, Monaco, random)!).ToList();
        Assert.InRange(rolls.Count(p => p.Traps) / 3000.0, 0.62, 0.71);
    }

    /// <summary>Every roll comes up: NextDouble is 0</summary>
    private sealed class SureRandom : Random
    {
        public override double NextDouble() => 0;
        public override int Next(int maxValue) => 0;
    }

    /// <summary>No roll comes up</summary>
    private sealed class NeverRandom : Random
    {
        public override double NextDouble() => 0.999999;
    }

    // ----- race.ini -----

    private string[] RaceIni(DragRaceIntent intent)
    {
        var cfg = _temp.Combine("cfg");
        Directory.CreateDirectory(cfg);
        Assert.True(new IniModificationService(cfg, _temp.Combine("restore")).ApplyIntent(intent));
        return File.ReadAllLines(Path.Combine(cfg, "race.ini"));
    }

    [Fact]
    public void The_police_go_into_race_ini_after_the_racers_and_the_race_mode_is_told_who_they_are()
    {
        var lines = RaceIni(new DragRaceIntent
        {
            PlayerCarId = "car_a", OpponentCarId = "car_b", PlayerName = "P", OpponentName = "O", RaceType = RaceType.Circuit,
            TrackId = "ks_highlands", TrackConfig = "layout_int", RaceTime = Night,
            Police = new RacePolice { CarId = "dodge_monaco_74", Skins = ["police_a", "police_b"], SpotShare = 0.4 }
        });

        Assert.Contains("CARS=4", lines);
        Assert.Contains("[CAR_2]", lines);
        Assert.Contains("[CAR_3]", lines);
        Assert.DoesNotContain("[CAR_4]", lines);
        Assert.Equal(2, lines.Count(l => l == "MODEL=dodge_monaco_74"));
        Assert.Contains("SKIN=police_b", lines);
        Assert.Contains($"DRIVER_NAME={IniModificationService.PoliceDriverName}", lines);
        Assert.Contains("POLICE=2,3", lines);
        Assert.Contains("POLICE_SPOT=0.400", lines);
        Assert.Contains("POLICE_MODE=PATROL", lines);
        // 21:00: 16 degrees an hour past 13:00
        Assert.Contains("SUN_ANGLE=128.00", lines);
    }

    [Fact]
    public void A_drag_race_has_no_police_and_a_race_without_a_time_is_raced_at_noon()
    {
        var lines = RaceIni(new DragRaceIntent
        {
            PlayerCarId = "car_a", OpponentCarId = "car_b", PlayerName = "P", OpponentName = "O", RaceType = RaceType.DragRace,
            Police = new RacePolice { CarId = "dodge_monaco_74", Skins = ["police_a"], SpotShare = 0.4 }
        });

        Assert.Contains("CARS=2", lines);
        Assert.DoesNotContain("[CAR_2]", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("POLICE"));
        Assert.Contains("SUN_ANGLE=-16.00", lines);
        Assert.Equal(-80.0, IniModificationService.SunAngle(new DateTime(1970, 1, 1, 8, 0, 0)));
    }

    // ----- what a chase does to the career -----

    private readonly RaceResultProcessorTests.FakeRepository _repository = new();

    private RaceResultProcessor Processor() =>
        new(_repository, new RaceSessionRepository(new SaveDatabase(_temp.Combine("unused-saves"))), new RaceFakes.FakeCareer(), new RaceFakes.FakeEvents());

    private static (GameState State, RaceContext Context, Car PlayerCar, Opponent Rival, Car RivalCar) World(decimal wager = 500m, bool pinkSlip = false)
    {
        var world = RaceFakes.World(wager, pinkSlip, RaceType.Circuit);
        world.State.Date = Night;
        return (world.State, world.Context, world.PlayerCar, world.Rival, world.RivalCar);
    }

    /// <summary>A road race the player won at the line, and how the chase went for each</summary>
    private static RaceResultJson Chase(string? player, string? rival, bool started = true)
    {
        var json = RaceResultValidatorTests.Fixture("1.1", "Player", "Rival");
        json["metadata"]!["schema_version"] = "1.5";
        json["session"]!["end_reason"] = player == PursuitOutcomes.Busted ? EndReasons.Busted : EndReasons.Finished;
        json["pursuit"] = new JObject
        {
            ["police"] = 2, ["started"] = started, ["started_at_s"] = 40.0, ["duration_s"] = 60.0,
            ["player"] = player, ["rival"] = rival
        };
        return JsonConvert.DeserializeObject<RaceResultJson>(json.ToString())!;
    }

    [Fact]
    public void Busted_after_winning_at_the_line_is_a_loss_a_fine_and_the_car_in_the_impound()
    {
        var (state, context, car, rival, _) = World();

        var messages = Processor().ProcessRaceResult(Chase(PursuitOutcomes.Busted, PursuitOutcomes.Escaped), context, state);

        Assert.Equal(1, state.Player.Stats.Losses);
        Assert.Equal(1_000_000m - 500m - PoliceRules.Fine(0), state.Player.Money);
        Assert.Equal(5500m, rival.Money);
        Assert.Equal(1, state.Player.Stats.PoliceBusts);
        Assert.True(car.IsImpounded);
        Assert.Equal(PoliceRules.ImpoundFeePerDay * PoliceRules.ImpoundDays(0), car.ImpoundFee);
        Assert.Equal(nameof(WinCondition.PlayerBusted), _repository.Recorded.Single().WinCondition);
        var busted = Assert.Single(messages, m => m.Title == "Busted");
        Assert.True(busted.TowedToGarage);
    }

    [Fact]
    public void A_busted_rival_loses_the_race_pays_a_fine_and_waits_for_the_car()
    {
        var (state, context, car, rival, rivalCar) = World();
        var result = Chase(PursuitOutcomes.Escaped, PursuitOutcomes.Busted);
        // The rival was ahead at the line: it doesn't matter, the police have him
        result.Participants[0].Performance.FinalPosition = 2;
        result.Participants[1].Performance.FinalPosition = 1;

        Processor().ProcessRaceResult(result, context, state);

        Assert.Equal(1, state.Player.Stats.Wins);
        Assert.Equal(5000m - 500m - PoliceRules.Fine(0), rival.Money);
        Assert.True(rivalCar.IsImpounded);
        Assert.False(car.IsImpounded);
        Assert.Equal(1, rival.Stats.PoliceBusts);
        Assert.Equal(1, state.Player.Stats.PoliceEscapes);
    }

    [Fact]
    public void Both_busted_is_no_contest_and_both_pay()
    {
        var (state, context, car, rival, rivalCar) = World();

        Processor().ProcessRaceResult(Chase(PursuitOutcomes.Busted, PursuitOutcomes.Busted), context, state);

        Assert.Equal(0, state.Player.Stats.Wins + state.Player.Stats.Losses);
        Assert.Equal(1_000_000m - PoliceRules.Fine(0), state.Player.Money);
        Assert.Equal(5000m - PoliceRules.Fine(0), rival.Money);
        Assert.True(car.IsImpounded && rivalCar.IsImpounded);
        Assert.Equal(nameof(WinCondition.BothBusted), _repository.Recorded.Single().WinCondition);
    }

    [Fact]
    public void Getting_away_is_worth_reputation_and_the_race_stands()
    {
        var (state, context, car, _, _) = World();
        var before = state.Player.Stats.Reputation;

        var messages = Processor().ProcessRaceResult(Chase(PursuitOutcomes.Escaped, PursuitOutcomes.Escaped), context, state);

        Assert.Equal(1, state.Player.Stats.Wins);
        Assert.Equal(1, state.Player.Stats.PoliceEscapes);
        Assert.False(car.IsImpounded);
        Assert.True(state.Player.Stats.Reputation > before);
        Assert.Contains(messages, m => m.Title == "Got Away");
    }

    [Fact]
    public void A_patrol_that_never_showed_up_changes_nothing()
    {
        var (state, context, car, _, _) = World();

        var messages = Processor().ProcessRaceResult(Chase(null, null, started: false), context, state);

        Assert.Equal(1, state.Player.Stats.Wins);
        Assert.Equal(1_000_000m + 500m, state.Player.Money);
        Assert.Equal(0, state.Player.Stats.PoliceBusts + state.Player.Stats.PoliceEscapes);
        Assert.False(car.IsImpounded);
        Assert.DoesNotContain(messages, m => m.Title is "Busted" or "Got Away" or "The Police");
    }

    [Fact]
    public void A_busted_player_who_loses_a_pink_slip_hands_over_the_car_and_the_rival_collects_it()
    {
        var (state, context, car, rival, _) = World(wager: 0m, pinkSlip: true);

        Processor().ProcessRaceResult(Chase(PursuitOutcomes.Busted, PursuitOutcomes.Escaped), context, state);

        Assert.DoesNotContain(car, state.Player.Cars);
        Assert.Contains(car, rival.Cars);
        Assert.False(car.IsImpounded);
        Assert.Equal(1_000_000m - PoliceRules.Fine(0), state.Player.Money);
    }

    [Fact]
    public void A_fine_the_player_cannot_pay_goes_on_the_impounds_bill()
    {
        var (state, context, car, _, _) = World(wager: 0m);
        state.Player.Money = 200m;

        Processor().ProcessRaceResult(Chase(PursuitOutcomes.Busted, PursuitOutcomes.Escaped), context, state);

        Assert.Equal(0m, state.Player.Money);
        Assert.Equal(PoliceRules.ImpoundFeePerDay * PoliceRules.ImpoundDays(0) + PoliceRules.Fine(0) - 200m, car.ImpoundFee);
    }

    [Fact]
    public async Task An_impounded_car_cannot_be_sold()
    {
        var (state, _, car, _, _) = World();
        PoliceRules.Impound(car, Night, 0);

        // Refused before the market or the clock are asked anything
        var sale = new Street_Rod_AC.Services.Market.CarSaleService(null!, null!, _repository);
        var result = await sale.SellToDealerAsync(state, car);

        Assert.False(result.Succeeded);
        Assert.Contains(car, state.Player.Cars);
    }
}
