using Newtonsoft.Json;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Services.Race;
using Street_Rod_AC.Services.Race.Validation;

namespace StreetRodAC.Tests;

/// <summary>
/// The race mode's result file, as the mode writes it, read the way the game reads it. The fixtures come out of the
/// mode itself (tools/sr_race_harness/test_chase.py --golden and test_strip.py --golden, which also fail when the
/// mode's output drifts from them): a road race with the police, won at the line after getting away; a bracket race
/// won on the dial; a test-and-tune of two passes.
/// </summary>
public sealed class RaceResultContractTests : IDisposable
{
    private static string Golden => Fixture("sr_race_result.json");

    private static string Fixture(string name) => Path.Combine(RepoPaths.Root, "tests", "StreetRodAC.Tests", "Fixtures", name);

    private async Task<RaceResultJson> ValidAsync(string fixture)
    {
        var path = _temp.File(Guid.NewGuid() + ".json", File.ReadAllText(Fixture(fixture)));
        var validation = await new RaceResultValidator().ValidateFileAsync(path);
        Assert.True(validation.IsValid, string.Join("; ", validation.Errors));
        return validation.ParsedResult!;
    }

    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task What_the_race_mode_writes_passes_validation_and_reads_into_every_field()
    {
        // Under a session-id name in a folder of its own, as it lands in the inbox
        var text = File.ReadAllText(Golden);
        var path = _temp.File(Guid.NewGuid() + ".json", text);

        var validation = await new RaceResultValidator().ValidateFileAsync(path);

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors));
        var result = validation.ParsedResult!;
        Assert.Equal("1.7", result.Metadata.SchemaVersion);
        Assert.Equal("sr_race_manager", result.Metadata.Source);
        Assert.True(Guid.TryParse(result.Session.SessionId, out _));
        Assert.Equal(Guid.Parse("6f1c2d3e-4b5a-4c6d-8e7f-90a1b2c3d4e5"), Guid.Parse(result.Session.ContextId!));
        Assert.True(RaceResultValidator.TryParseTimestamp(result.Session.StartTimestamp, out _));
        Assert.Equal(EndReasons.Finished, result.Session.EndReason);
        Assert.Equal("ROAD", result.Session.RaceType);

        var player = Assert.Single(result.Participants, p => p.IsPlayer == true);
        Assert.Equal(0, player.CarIndex);
        Assert.Equal(1, player.Performance.FinalPosition);
        Assert.Equal(1, player.Performance.LapsCompleted);
        Assert.True(player.Performance.DistanceKm > 0);
        Assert.False(player.Crash.Crashed);
        Assert.Empty(player.Crash.CrashIntensitiesG);
        Assert.Equal(4, player.Condition!.BodyDamageKmh!.Count);
        Assert.Equal(1000.0, player.Condition.EngineLife);
        Assert.Equal(new int?[] { 0, 1, 2, 3 }, player.Condition.Wheels!.Select(w => w.Wheel));
        Assert.All(player.Condition.Wheels!, w => Assert.NotNull(w.TyreWear));
        // Step 14: the tank, the dirt, the engine's heat and oil
        Assert.True(player.Condition.MaxFuelLitres > 0);
        Assert.NotNull(player.Condition.Dirt);
        Assert.NotNull(player.Condition.Heat!.PeakWaterC);
        Assert.Equal(1.0, player.Condition.Heat.LowestOilSupply);
        // A road race has no timeslip
        Assert.Null(player.Timeslip);

        Assert.NotNull(result.Pursuit);
        Assert.True(result.Pursuit!.PlayerEscaped);
        Assert.Equal(2, result.Pursuit.Police);
    }

    [Theory]
    [InlineData("sr_race_result.json")]
    [InlineData("sr_race_bracket_result.json")]
    [InlineData("sr_race_tune_result.json")]
    public void Nothing_the_race_mode_writes_is_left_unread(string fixture)
    {
        // Every key in the file has a property to go to: a key the game drops is a contract that drifted
        var settings = new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error };
        var result = JsonConvert.DeserializeObject<RaceResultJson>(File.ReadAllText(Fixture(fixture)), settings);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task A_bracket_race_as_the_mode_writes_it_is_decided_the_same_way_by_the_career()
    {
        var result = await ValidAsync("sr_race_bracket_result.json");

        Assert.Equal(RaceTypes.Drag, result.Session.RaceType);
        var player = Assert.Single(result.Participants, p => p.IsPlayer == true);
        var rival = Assert.Single(result.Participants, p => p.IsPlayer == false);
        Assert.NotNull(player.DialInSeconds);
        Assert.False(player.Breakout);
        Assert.NotNull(player.Timeslip!.GreenSeconds);
        Assert.NotNull(rival.Timeslip!.GreenSeconds);

        // The mode's call (its final positions) and the career's (the slips and the dial-ins) agree
        var decided = BracketRules.Decide(player.Timeslip, player.DialInSeconds!.Value, rival.Timeslip, rival.DialInSeconds!.Value)!.Value;
        Assert.Equal(player.Performance.FinalPosition == 1, decided.PlayerWon);
        Assert.Equal(rival.Breakout, decided.OpponentBrokeOut);
    }

    [Fact]
    public async Task A_test_and_tune_as_the_mode_writes_it_is_one_car_and_its_passes()
    {
        var result = await ValidAsync("sr_race_tune_result.json");

        Assert.Equal(RaceTypes.TestAndTune, result.Session.RaceType);
        var player = Assert.Single(result.Participants);
        Assert.True(player.IsPlayer);
        Assert.Equal([1, 2], player.Passes!.Select(p => p.Pass!.Value));
        Assert.All(player.Passes!, p => Assert.NotNull(p.QuarterMileSeconds));
        Assert.Equal(player.Passes!.Min(p => p.QuarterMileSeconds), player.Timeslip!.QuarterMileSeconds);
        Assert.Null(player.DialInSeconds);
    }
}
