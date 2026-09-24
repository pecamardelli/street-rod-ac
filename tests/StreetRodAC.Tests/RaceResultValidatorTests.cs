using Newtonsoft.Json.Linq;
using Street_Rod_AC.Services.Race.Validation;

namespace StreetRodAC.Tests;

/// <summary>Result files as the Lua app writes them (schema 1.0 and 1.1), and broken ones</summary>
public sealed class RaceResultValidatorTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly RaceResultValidator _validator = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>A valid result file; schema 1.1 adds context_id, car_index and is_player</summary>
    public static JObject Fixture(string schema = "1.1", string player = "Player", string opponent = "Rival")
    {
        var v11 = schema == "1.1";
        JObject Participant(string name, int index, int position) => new()
        {
            ["driver_name"] = name,
            ["car_name"] = "car_" + index,
            ["car_index"] = v11 ? index : null,
            ["is_player"] = v11 ? index == 0 : null,
            ["performance"] = new JObject
            {
                ["final_position"] = position, ["laps_completed"] = 1, ["best_lap_time_ms"] = 12000.5,
                ["total_race_time_ms"] = 12000.5, ["max_speed_kmh"] = 180.2, ["distance_km"] = 0.402, ["fuel_consumed_liters"] = 0.3
            },
            ["crash"] = new JObject { ["crashed"] = false, ["crash_intensities_g"] = new JArray(), ["max_crash_intensity_g"] = 0.0 }
        };

        var json = new JObject
        {
            ["metadata"] = new JObject { ["schema_version"] = schema, ["script_version"] = v11 ? "2.2.0" : "2.1.0", ["source"] = "sr_race_manager", ["generated_at"] = "2026-09-24T18:30:20Z" },
            ["session"] = new JObject
            {
                ["session_id"] = Guid.NewGuid().ToString(), ["start_timestamp"] = "2026-09-24T18:30:05Z", ["end_timestamp"] = "2026-09-24T18:30:17Z",
                ["duration_seconds"] = 12.3, ["track_id"] = "ks_drag", ["track_layout"] = "drag1000"
            },
            ["participants"] = new JArray(Participant(player, 0, 1), Participant(opponent, 1, 2))
        };
        if (v11) ((JObject)json["session"]!)["context_id"] = Guid.NewGuid().ToString("D");
        if (!v11)
        {
            foreach (var p in json["participants"]!.Cast<JObject>())
            {
                p.Remove("car_index");
                p.Remove("is_player");
            }
        }

        return json;
    }

    private string Write(string text, string? name = null) => _temp.File(name ?? Guid.NewGuid() + ".json", text);

    private Task<ValidationResult> Validate(JObject json) => _validator.ValidateFileAsync(Write(json.ToString()));

    [Theory]
    [InlineData("1.0")]
    [InlineData("1.1")]
    public async Task A_good_file_of_either_schema_passes(string schema)
    {
        var result = await Validate(Fixture(schema));
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Equal(2, result.ParsedResult!.Participants.Count);
        if (schema == "1.1")
        {
            Assert.True(result.ParsedResult.Participants[0].IsPlayer);
            Assert.Equal(1, result.ParsedResult.Participants[1].CarIndex);
            Assert.NotNull(result.ParsedResult.Session.ContextId);
        }
        else
        {
            Assert.Null(result.ParsedResult.Participants[0].IsPlayer);
            Assert.Null(result.ParsedResult.Session.ContextId);
        }
    }

    [Theory]
    [InlineData("crash")]
    [InlineData("performance")]
    public async Task A_participant_block_that_is_null_is_rejected(string block)
    {
        var json = Fixture();
        json["participants"]![1]![block] = JValue.CreateNull();
        var result = await Validate(json);
        Assert.False(result.IsValid);
        Assert.Equal(ValidationFailureReason.InvalidParticipantData, result.FailureReason);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("\"NaN\"")]
    [InlineData("-Infinity")]
    public async Task An_impossible_distance_is_rejected(string distance)
    {
        var text = Fixture().ToString().Replace("\"distance_km\": 0.402", "\"distance_km\": " + distance);
        Assert.Contains("\"distance_km\": " + distance, text);
        var result = await _validator.ValidateFileAsync(Write(text));
        Assert.False(result.IsValid);
        Assert.Equal(ValidationFailureReason.InvalidParticipantData, result.FailureReason);
    }

    [Theory]
    [InlineData("24/09/2026")]
    [InlineData("yesterday")]
    [InlineData("2026-13-45T00:00:00Z")]
    public async Task A_timestamp_that_is_not_a_date_is_rejected(string timestamp)
    {
        var json = Fixture();
        json["session"]!["start_timestamp"] = timestamp;
        var result = await Validate(json);
        Assert.False(result.IsValid);
        Assert.Equal(ValidationFailureReason.InvalidTimestamp, result.FailureReason);
    }

    [Fact]
    public void An_iso_timestamp_with_z_is_utc()
    {
        Assert.True(RaceResultValidator.TryParseTimestamp("2026-09-24T18:30:05Z", out var at));
        Assert.Equal(DateTimeKind.Utc, at.Kind);
        Assert.Equal(new DateTime(2026, 9, 24, 18, 30, 5, DateTimeKind.Utc), at);
    }

    [Fact]
    public void A_timestamp_is_read_the_same_whatever_the_machines_culture()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
            Assert.True(RaceResultValidator.TryParseTimestamp("2026-09-24T18:30:05Z", out var at));
            Assert.Equal(2026, at.Year);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public async Task Two_participants_claiming_to_be_the_player_are_rejected()
    {
        var json = Fixture();
        json["participants"]![1]!["is_player"] = true;
        var result = await Validate(json);
        Assert.False(result.IsValid);
        Assert.Equal(ValidationFailureReason.InvalidParticipantData, result.FailureReason);
    }

    [Theory]
    [InlineData("result.json")]
    [InlineData("1234.json")]
    [InlineData("d3b07384-d9a0-4c9b-8f7e-2a1c5e0b9f11.json.tmp")]
    [InlineData("d3b07384-d9a0-4c9b-8f7e-2a1c5e0b9f11.txt")]
    public async Task A_file_not_named_by_a_uuid_is_ignored(string name)
    {
        var result = await _validator.ValidateFileAsync(Write(Fixture().ToString(), name));
        Assert.False(result.IsValid);
        Assert.Equal(ValidationFailureReason.InvalidFileName, result.FailureReason);
    }

    [Fact]
    public async Task An_empty_file_is_ignored_and_bad_json_is_invalid()
    {
        Assert.Equal(ValidationFailureReason.ZeroSize, (await _validator.ValidateFileAsync(Write(""))).FailureReason);
        Assert.Equal(ValidationFailureReason.InvalidJson, (await _validator.ValidateFileAsync(Write("{\"metadata\": {"))).FailureReason);
        Assert.Equal(ValidationFailureReason.InvalidJson, (await _validator.ValidateFileAsync(Write("null"))).FailureReason);
    }

    [Fact]
    public async Task Missing_or_wrong_header_fields_are_rejected_with_their_reason()
    {
        var noSchema = Fixture();
        noSchema["metadata"]!["schema_version"] = "";
        Assert.Equal(ValidationFailureReason.MissingSchemaVersion, (await Validate(noSchema)).FailureReason);

        var wrongSource = Fixture();
        wrongSource["metadata"]!["source"] = "someone_else";
        Assert.Equal(ValidationFailureReason.WrongSource, (await Validate(wrongSource)).FailureReason);

        var badSession = Fixture();
        badSession["session"]!["session_id"] = "not-a-uuid";
        Assert.Equal(ValidationFailureReason.MissingSessionId, (await Validate(badSession)).FailureReason);

        var nullSession = Fixture();
        nullSession["session"] = JValue.CreateNull();
        Assert.False((await Validate(nullSession)).IsValid);

        var three = Fixture();
        ((JArray)three["participants"]!).Add(three["participants"]![0]!.DeepClone());
        Assert.Equal(ValidationFailureReason.InvalidParticipantCount, (await Validate(three)).FailureReason);

        var noName = Fixture();
        noName["participants"]![0]!["driver_name"] = " ";
        Assert.False((await Validate(noName)).IsValid);
    }
}
