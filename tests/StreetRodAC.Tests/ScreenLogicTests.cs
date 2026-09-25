using System.Windows;
using Street_Rod_AC.Audio;
using Street_Rod_AC.Helpers;
using Street_Rod_AC.Models.AC;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Parts.Export;
using Street_Rod_AC.Screens.Diner;
using Street_Rod_AC.Screens.Shared;
using Street_Rod_AC.Services.Opponents;
using Street_Rod_AC.Services.Race;

namespace StreetRodAC.Tests;

public class RaceSetupBuilderTests
{
    [Fact]
    public void RacesAs_an_opponent_in_another_model_races_in_its_own_folder()
    {
        var data = RaceCarData.AsAuthored("car_b", "unused");
        var (racesAs, raceData) = RaceSetupBuilder.RacesAs("car_a", "car_b", data);

        Assert.Equal("car_b", racesAs);
        Assert.Same(data, raceData);
    }

    [Theory]
    [InlineData("car_a", "car_a")]
    [InlineData("car_a", "CAR_A")]
    public void RacesAs_an_opponent_in_the_players_model_races_in_a_marked_copy(string player, string opponent)
    {
        var (racesAs, raceData) = RaceSetupBuilder.RacesAs(player, opponent, null);

        Assert.Equal(AcCarFolder.CloneIdFor(opponent), racesAs);
        Assert.NotNull(raceData);
        Assert.Equal(opponent, raceData!.CarId);
        Assert.Equal(racesAs, raceData.CloneId);
    }

    [Fact]
    public void RacesAs_a_copy_keeps_the_opponents_build()
    {
        var data = RaceCarData.AsAuthored("car_a", "whatever");
        var (racesAs, raceData) = RaceSetupBuilder.RacesAs("car_a", "car_a", data);

        Assert.Same(data.Build, raceData!.Build);
        Assert.Equal(racesAs, raceData.CloneId);
    }

    [Fact]
    public void BuildIntent_and_its_context_describe_the_same_race()
    {
        var player = new Car("car_a") { SkinId = "red" };
        var opponent = new Car("car_b");
        var entry = new RaceEntry
        {
            PlayerName = "Bob",
            PlayerCar = player,
            OpponentName = "Ann",
            OpponentCarId = "car_b",
            OpponentCar = opponent,
            OpponentAI = new AssettoCorsaAIParameters { AILevel = 95, AIAggression = 40 },
            TrackId = "ks_drag",
            TrackConfig = "drag1000",
            RaceType = RaceType.DragRace,
            CashWager = 500,
            EventId = "evt",
            IsEventOnlyOpponent = true
        };

        var intent = RaceSetupBuilder.BuildIntent(entry, "car_b", new List<RaceCarData>());

        Assert.Equal("car_a", intent.PlayerCarId);
        Assert.Equal("red", intent.PlayerSkin);
        Assert.Equal("car_b", intent.OpponentCarId);
        Assert.Equal(95, intent.OpponentAILevel);
        Assert.Equal(40, intent.OpponentAIAggression);
        Assert.Equal(500, intent.CashWager);

        var context = Assert.IsType<RaceContext>(intent.Metadata["RaceContext"]);
        Assert.Equal(player.InstanceId, context.PlayerCarInstanceId);
        Assert.Equal(opponent.InstanceId, context.OpponentCarInstanceId);
        Assert.Equal(intent.TrackId, context.TrackId);
        Assert.Equal(intent.TrackConfig, context.TrackConfig);
        Assert.Equal(intent.CashWager, context.CashWager);
        Assert.Equal("evt", context.EventId);
        Assert.True(context.IsEventOnlyOpponent);
    }

    [Fact]
    public void BuildIntent_an_event_entrant_without_a_car_on_the_books_has_no_instance()
    {
        var entry = new RaceEntry { PlayerCar = new Car("car_a"), OpponentCarId = "car_b" };
        var intent = RaceSetupBuilder.BuildIntent(entry, "car_b", new List<RaceCarData>());

        Assert.Equal(Guid.Empty, intent.OpponentCarInstanceId);
        Assert.Equal(Guid.Empty, ((RaceContext)intent.Metadata["RaceContext"]).OpponentCarInstanceId);
    }

    private static TrackInfo Track(string id, string run, params string[] layouts) => new()
    {
        TrackId = id,
        Run = run,
        Configurations = layouts.Select(l => new TrackConfiguration { FolderName = l }).ToList()
    };

    private static readonly TrackInfo[] Installed =
    [
        Track("ks_drag", "dragstrip", "drag1000", "drag400"),
        Track("other_drag", "dragstrip"),
        Track("monza", "clockwise", "gp", "junior"),
        Track("brands", "clockwise")
    ];

    [Theory]
    [InlineData(RaceType.DragRace, "monza", "monza", "gp")] // the named track, on its first layout, whatever the race
    [InlineData(RaceType.Sprint, "BRANDS", "brands", null)]
    [InlineData(RaceType.DragRace, null, "ks_drag", "drag1000")] // AC's own strip is the drag default
    [InlineData(RaceType.DragRace, "not_installed", "ks_drag", "drag1000")]
    public void PickTrack(RaceType type, string? wanted, string expectedTrack, string? expectedLayout)
    {
        var pick = RaceSetupBuilder.PickTrack(Installed, type, wanted, seed: 7);

        Assert.Equal<(string, string?)?>((expectedTrack, expectedLayout), pick);
    }

    [Fact]
    public void PickTrack_a_road_race_gets_a_road_layout_and_the_same_one_for_the_same_seed()
    {
        var first = RaceSetupBuilder.PickTrack(Installed, RaceType.Circuit, null, seed: 12345);
        var again = RaceSetupBuilder.PickTrack(Installed, RaceType.Circuit, null, seed: 12345);

        Assert.NotNull(first);
        Assert.Contains(first!.Value.TrackId, new[] { "monza", "brands" });
        Assert.Equal(first, again);

        // Negative seeds too
        Assert.NotNull(RaceSetupBuilder.PickTrack(Installed, RaceType.Circuit, null, seed: int.MinValue));
    }

    [Fact]
    public void PickTrack_nothing_suitable_installed_is_null()
    {
        Assert.Null(RaceSetupBuilder.PickTrack([Track("monza", "clockwise")], RaceType.DragRace, null, 1));
        Assert.Null(RaceSetupBuilder.PickTrack([], RaceType.Sprint, "monza", 1));
    }
}

public class EngineEventsTests
{
    private const string Engine = "{11111111-1111-1111-1111-111111111111}";
    private const string Limiter = "{22222222-2222-2222-2222-222222222222}";
    private const string Backfire = "{33333333-3333-3333-3333-333333333333}";
    private const string OtherEngine = "{44444444-4444-4444-4444-444444444444}";

    [Fact]
    public void Read_finds_the_donors_events()
    {
        var text = $"{OtherEngine} event:/cars/other/engine_ext\r\n{Engine} event:/cars/mycar/engine_ext\r\n" +
                   $"{Limiter} event:/cars/MyCar/limiter\n{Backfire} event:/cars/mycar/backfire_ext\n";

        var events = EngineEvents.Read(text, "mycar");

        Assert.Equal(Guid.Parse(Engine), events.Engine);
        Assert.Equal(Guid.Parse(Limiter), events.Limiter);
        Assert.Equal(Guid.Parse(Backfire), events.Backfire);
    }

    [Fact]
    public void Read_without_the_donors_engine_takes_any_engine_but_not_its_limiter()
    {
        var text = $"{OtherEngine} event:/cars/other/engine_ext\n{Limiter} event:/cars/other/limiter\n";

        var events = EngineEvents.Read(text, "mycar");

        Assert.Equal(Guid.Parse(OtherEngine), events.Engine);
        Assert.Null(events.Limiter);
        Assert.Null(events.Backfire);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n\n   \n")]
    [InlineData("not-a-guid event:/cars/mycar/engine_ext")]
    [InlineData("{11111111-1111-1111-1111-111111111111}")] // no path
    [InlineData(" {11111111-1111-1111-1111-111111111111} ")]
    [InlineData("{11111111-1111-1111-1111-11111111111} event:/cars/mycar/engine_ext")] // one digit short
    [InlineData("{11111111-1111-1111-1111-111111111111} event:/ui/mycar/engine_ext")] // not a car
    [InlineData("{11111111-1111-1111-1111-111111111111} event:/cars/mycar/extra/engine_ext")] // too deep
    [InlineData("{11111111-1111-1111-1111-111111111111} event:/cars/engine_ext")] // too shallow
    [InlineData("\0\0\0 événement")]
    public void Read_skips_what_it_cannot_read(string text)
    {
        var events = EngineEvents.Read(text, "mycar");

        Assert.Equal(new EngineEvents(null, null, null), events);
    }
}

public class EngineLoudnessTests
{
    private static double[] Flat(double value) =>
        Enumerable.Repeat(value, EngineLoudness.RpmGrid.Length * EngineLoudness.ThrottleGrid.Length).ToArray();

    [Fact]
    public void Interpolate_hits_the_grid_points()
    {
        var grid = EngineLoudness.TargetGrid;
        Assert.Equal(grid[0], EngineLoudness.Interpolate(grid, 800, 0), 9);
        Assert.Equal(grid[2], EngineLoudness.Interpolate(grid, 800, 1), 9);
        Assert.Equal(grid[^1], EngineLoudness.Interpolate(grid, 7500, 1), 9);
        Assert.Equal(grid[3 * 3 + 1], EngineLoudness.Interpolate(grid, 3500, 0.5), 9);
    }

    [Fact]
    public void Interpolate_is_linear_between_points_and_flat_past_the_ends()
    {
        var grid = EngineLoudness.TargetGrid;
        var between = EngineLoudness.Interpolate(grid, (800 + 1500) / 2.0, 0);
        Assert.Equal((grid[0] + grid[3]) / 2, between, 9);

        Assert.Equal(EngineLoudness.Interpolate(grid, 800, 0), EngineLoudness.Interpolate(grid, 0, -5), 9);
        Assert.Equal(EngineLoudness.Interpolate(grid, 7500, 1), EngineLoudness.Interpolate(grid, 20000, 3), 9);
    }

    [Theory]
    [InlineData(double.NaN, 0.5)]
    [InlineData(3000, double.NaN)]
    [InlineData(double.PositiveInfinity, double.NegativeInfinity)]
    public void Interpolate_never_throws_on_odd_input(double rpm, double throttle)
    {
        var value = EngineLoudness.Interpolate(EngineLoudness.TargetGrid, rpm, throttle);
        Assert.True(double.IsFinite(value));
    }

    [Fact]
    public void GainDb_brings_a_bank_to_the_target_within_the_limits()
    {
        // A bank that is the target everywhere needs nothing
        Assert.Equal(0, EngineLoudness.GainDb(new EngineLevel(EngineLoudness.TargetGrid.ToArray()), 3000, 0.3), 9);

        // One far too quiet is boosted at most 20 dB, one far too loud cut at most 12
        Assert.Equal(20, EngineLoudness.GainDb(new EngineLevel(Flat(-200)), 3000, 0.3), 9);
        Assert.Equal(-12, EngineLoudness.GainDb(new EngineLevel(Flat(50)), 3000, 0.3), 9);

        // In between, the difference
        var level = EngineLoudness.TargetGrid.Select(v => v - 3).ToArray();
        Assert.Equal(3, EngineLoudness.GainDb(new EngineLevel(level), 4500, 1), 9);
    }

    [Fact]
    public void IsWholeGrid_only_for_a_full_grid_of_numbers()
    {
        Assert.True(EngineLoudness.IsWholeGrid(new EngineLevel(Flat(-30))));
        Assert.False(EngineLoudness.IsWholeGrid(null));
        Assert.False(EngineLoudness.IsWholeGrid(new EngineLevel(null!)));
        Assert.False(EngineLoudness.IsWholeGrid(new EngineLevel([])));
        Assert.False(EngineLoudness.IsWholeGrid(new EngineLevel(Flat(-30).Skip(1).ToArray())));
        Assert.False(EngineLoudness.IsWholeGrid(new EngineLevel(Flat(-30).Append(-30).ToArray())));

        var withNaN = Flat(-30);
        withNaN[5] = double.NaN;
        Assert.False(EngineLoudness.IsWholeGrid(new EngineLevel(withNaN)));

        var withInfinity = Flat(-30);
        withInfinity[0] = double.NegativeInfinity;
        Assert.False(EngineLoudness.IsWholeGrid(new EngineLevel(withInfinity)));
    }
}

public class MapPanelFitTests
{
    private static void AssertInside(Rect crop)
    {
        Assert.True(crop.X >= -1e-9 && crop.Y >= -1e-9, $"{crop} starts outside the picture");
        Assert.True(crop.Right <= 1 + 1e-9 && crop.Bottom <= 1 + 1e-9, $"{crop} runs off the picture");
    }

    [Fact]
    public void Fit_grows_the_anchor_to_the_panels_shape_about_its_middle()
    {
        // A square picture, a square anchor in the middle, a panel twice as wide as tall
        var crop = MapPanel.Fit(new Rect(0.4, 0.4, 0.2, 0.2), 2.0, 1000, 1000);

        Assert.Equal(0.4, crop.Width, 9);
        Assert.Equal(0.2, crop.Height, 9);
        Assert.Equal(0.5, crop.X + crop.Width / 2, 9);
        Assert.Equal(0.5, crop.Y + crop.Height / 2, 9);
    }

    [Fact]
    public void Fit_slides_back_inside_the_picture_at_an_edge()
    {
        var crop = MapPanel.Fit(new Rect(0.0, 0.0, 0.2, 0.2), 2.0, 1000, 1000);

        AssertInside(crop);
        Assert.Equal(0.0, crop.X, 9);
        Assert.Equal(0.4, crop.Width, 9);
    }

    [Theory]
    [InlineData(100.0)] // a very wide panel against a square picture
    [InlineData(0.01)] // a very tall one
    [InlineData(16.0 / 9)]
    [InlineData(1.0)]
    public void Fit_keeps_the_panels_shape_and_stays_inside_at_extreme_aspects(double aspect)
    {
        var crop = MapPanel.Fit(new Rect(0.3, 0.3, 0.4, 0.4), aspect, 1600, 1200);

        AssertInside(crop);
        // The shape on the ground is the panel's
        Assert.Equal(aspect, crop.Width * 1600 / (crop.Height * 1200), 6);
    }

    [Theory]
    [InlineData(double.NaN, 1000, 1000)]
    [InlineData(0, 1000, 1000)]
    [InlineData(-1, 1000, 1000)]
    [InlineData(double.PositiveInfinity, 1000, 1000)]
    [InlineData(2, 0, 1000)]
    [InlineData(2, 1000, double.NaN)]
    public void Fit_leaves_the_anchor_alone_when_there_is_nothing_to_fit_to(double aspect, double width, double height)
    {
        var anchor = new Rect(0.1, 0.2, 0.3, 0.4);
        Assert.Equal(anchor, MapPanel.Fit(anchor, aspect, width, height));
    }

    [Fact]
    public void Fit_an_empty_anchor_stays_empty()
    {
        Assert.Equal(Rect.Empty, MapPanel.Fit(Rect.Empty, 2, 1000, 1000));
    }
}

public class ConditionDisplayTests
{
    [Theory]
    [InlineData(1.0, "100%", "Excellent")]
    [InlineData(0.9, "90%", "Excellent")]
    [InlineData(0.8, "80%", "Good")]
    [InlineData(0.6, "60%", "Fair")]
    [InlineData(0.45, "45%", "Poor")]
    [InlineData(0.1, "10%", "Very Poor")]
    [InlineData(-1.0, "0%", "Very Poor")]
    [InlineData(double.NaN, "0%", "Very Poor")]
    public void Of_a_condition(double condition, string percent, string label)
    {
        var text = ConditionDisplay.Of(condition);
        Assert.Equal(percent, text.Percent);
        Assert.Equal(label, text.Label);
    }

    [Fact]
    public void Of_a_car_is_its_overall_condition_on_the_markets_rule()
    {
        var car = new Car("car") { EngineHealth = 1, TransmissionHealth = 1, BodyCondition = 0.5, TireCondition = 0.5 };

        var text = ConditionDisplay.Of(car);

        Assert.Equal("75%", text.Percent);
        Assert.Equal("Good", text.Label);
        Assert.Equal("Good condition (75%)", text.Sentence);
    }

    [Fact]
    public void Of_a_car_that_will_not_run_says_what_is_wrong()
    {
        var blown = new Car("car") { EngineHealth = 0 };
        Assert.Equal("Blown engine", ConditionDisplay.Of(blown).Percent);
        Assert.Equal("Blown engine", ConditionDisplay.Of(blown).Sentence);

        var wrecked = new Car("car") { BodyDamageKmh = [100, 100, 50, 0] };
        Assert.Equal("Totaled", ConditionDisplay.Of(wrecked).Label);
    }

    [Fact]
    public void A_listing_and_the_car_bought_from_it_read_the_same()
    {
        const float listed = 0.73f;
        var bought = new Car("car") { EngineHealth = listed, TransmissionHealth = listed, BodyCondition = listed, TireCondition = listed };

        Assert.Equal(ConditionDisplay.Of(listed), ConditionDisplay.Of(bought));
    }
}

public class MatchupPowerTests
{
    private static CarDefinition Car(string bhp) => new() { Specs = new CarSpecsData { Bhp = bhp, Weight = "1500kg" } };

    [Fact]
    public void The_spec_sheet_is_used_without_a_dyno_figure()
    {
        var stats = MatchupCalculator.Compare(Car("200bhp"), Car("300bhp"));

        var power = stats.Single(s => s.Label == "Horsepower");
        Assert.Equal("200 HP", power.PlayerValue);
        Assert.Equal("300 HP", power.OpponentValue);
    }

    [Fact]
    public void A_cars_own_dyno_figure_beats_the_spec_sheet()
    {
        var stats = MatchupCalculator.Compare(Car("200bhp"), Car("300bhp"), playerPowerHp: 350, opponentPowerHp: 280);

        var power = stats.Single(s => s.Label == "Horsepower");
        Assert.Equal("350 HP", power.PlayerValue);
        Assert.Equal("280 HP", power.OpponentValue);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_dyno_figure_that_is_no_figure_falls_back_to_the_specs(double dyno)
    {
        var stats = MatchupCalculator.Compare(Car("200bhp"), Car("300bhp"), playerPowerHp: dyno);

        Assert.Equal("200 HP", stats.Single(s => s.Label == "Horsepower").PlayerValue);
    }
}

public class PathNamesReservedTests
{
    [Theory]
    [InlineData("CON", "CON_")]
    [InlineData("con", "con_")]
    [InlineData("Nul", "Nul_")]
    [InlineData("aux.db", "aux_.db")]
    [InlineData("PRN.tar.gz", "PRN_.tar.gz")]
    [InlineData("COM1", "COM1_")]
    [InlineData("lpt9", "lpt9_")]
    [InlineData("CON .txt", "CON_.txt")]
    [InlineData("COM0", "COM0")] // not a device
    [InlineData("COM10", "COM10")]
    [InlineData("Connor", "Connor")]
    [InlineData("icon", "icon")]
    [InlineData("my.con", "my.con")]
    public void Sanitize_device_names(string name, string expected) =>
        Assert.Equal(expected, PathNames.Sanitize(name));

    [Theory]
    [InlineData("abc .", "abc")]
    [InlineData("abc . .", "abc")]
    [InlineData(" . . ", "unnamed")]
    [InlineData("abc. ", "abc")]
    public void Sanitize_trims_spaces_and_dots_however_they_are_mixed(string name, string expected) =>
        Assert.Equal(expected, PathNames.Sanitize(name));

    [Fact]
    public void Sanitize_trims_again_after_capping()
    {
        var name = PathNames.Sanitize(new string('x', 48) + " .yy", maxLength: 50);
        Assert.Equal(new string('x', 48), name);
    }
}
