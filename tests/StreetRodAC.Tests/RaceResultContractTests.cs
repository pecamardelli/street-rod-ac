using Newtonsoft.Json;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Services.Race.Validation;

namespace StreetRodAC.Tests;

/// <summary>
/// The race mode's result file, as the mode writes it, read the way the game reads it. The fixture comes out of the
/// mode itself (tools/sr_race_harness/test_chase.py --golden, which also fails when the mode's output drifts from it):
/// a road race with the police, won at the line after getting away.
/// </summary>
public sealed class RaceResultContractTests : IDisposable
{
    private static string Golden => Path.Combine(RepoPaths.Root, "tests", "StreetRodAC.Tests", "Fixtures", "sr_race_result.json");

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
        Assert.Equal("1.5", result.Metadata.SchemaVersion);
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
        // A road race has no timeslip
        Assert.Null(player.Timeslip);

        Assert.NotNull(result.Pursuit);
        Assert.True(result.Pursuit!.PlayerEscaped);
        Assert.Equal(2, result.Pursuit.Police);
    }

    [Fact]
    public void Nothing_the_race_mode_writes_is_left_unread()
    {
        // Every key in the file has a property to go to: a key the game drops is a contract that drifted
        var settings = new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error };
        var result = JsonConvert.DeserializeObject<RaceResultJson>(File.ReadAllText(Golden), settings);
        Assert.NotNull(result);
    }
}
