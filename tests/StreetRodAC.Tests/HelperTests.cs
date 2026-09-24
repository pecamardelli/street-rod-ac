using System.Text;
using Street_Rod_AC.Helpers;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Parts;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Parts.Export;
using Street_Rod_AC.Screens.Diner;
using Street_Rod_AC.Services.Market;

namespace StreetRodAC.Tests;

public class AcSpecsTests
{
    [Theory]
    [InlineData("430bhp", 430.0)]
    [InlineData("335 hp", 335.0)]
    [InlineData("350hp @ 6000rpm", 350.0)]
    [InlineData("250 cv", 250.0)]
    [InlineData("147,5 bhp", 147.5)]
    [InlineData("1.250 hp", 1.25)]
    [InlineData("1,250.5 hp", 1250.5)]
    [InlineData("  90 HP ", 90.0)]
    [InlineData("150kW", 150 * 1.34102)]
    [InlineData("150 ps", 150.0)]
    public void Power(string text, double expected) => Assert.Equal(expected, AcSpecs.ParsePower(text)!.Value, 6);

    [Theory]
    [InlineData("1,250kg", 1250.0)]
    [InlineData("1.250 kg", 1250.0)]
    [InlineData("1,250.5 kg", 1250.5)]
    [InlineData("1.250,5 kg", 1250.5)]
    [InlineData("1450 kg", 1450.0)]
    [InlineData("1.5 t", 1500.0)]
    [InlineData("1.250 t", 1250.0)]
    [InlineData("2755 lbs", 2755 * 0.45359237)]
    public void Weight(string text, double expected) => Assert.Equal(expected, AcSpecs.ParseWeight(text)!.Value, 6);

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("0 hp")]
    [InlineData("   ")]
    [InlineData("-")]
    public void Not_a_figure(string? text)
    {
        Assert.Null(AcSpecs.ParsePower(text));
        Assert.Null(AcSpecs.ParseWeight(text));
        Assert.False(AcSpecs.TryParsePower(text, out _));
    }

    [Fact]
    public void Culture_does_not_matter()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal(147.5, AcSpecs.ParsePower("147,5 bhp"));
            Assert.Equal(1250, AcSpecs.ParseWeight("1,250kg"));
            Assert.Equal(2.5, AcSpecs.ParsePower("2.5 hp"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }
}

public class AcCarUiTests
{
    [Fact]
    public void Comments_trailing_commas_and_loose_types_are_read()
    {
        const string json = """
            {
              // a comment
              "name": "Test Car",
              /* block */
              "year": 1969.0,
              "tags": "muscle",
              "specs": { "bhp": "350hp @ 6000rpm", "weight": "1,250kg", },
            }
            """;
        var ui = AcCarUi.TryParse(json, out var error);
        Assert.Null(error);
        Assert.Equal("Test Car", AcCarUi.GetString(ui, "name"));
        Assert.Equal(1969, AcCarUi.GetInt(ui, "year"));
        Assert.Equal(["muscle"], AcCarUi.GetStrings(ui, "tags"));
        Assert.Equal("350hp @ 6000rpm", AcCarUi.GetString(AcCarUi.GetObject(ui, "specs"), "bhp"));
    }

    [Theory]
    [InlineData("1969", 1969)]
    [InlineData("1969.0", 1969)]
    [InlineData("\"1969\"", 1969)]
    [InlineData("1969.5", null)]
    [InlineData("\"sixty-nine\"", null)]
    [InlineData("99999999999", null)]
    [InlineData("null", null)]
    [InlineData("[1969]", null)]
    public void Year(string value, int? expected) =>
        Assert.Equal(expected, AcCarUi.GetInt(AcCarUi.TryParse($"{{\"year\": {value}}}", out _), "year"));

    [Fact]
    public void Tags_as_an_array_skip_what_is_not_text()
    {
        var ui = AcCarUi.TryParse("{\"tags\": [\"a\", 1, null, \"\", \"b\"]}", out _);
        Assert.Equal(["a", "b"], AcCarUi.GetStrings(ui, "tags"));
        Assert.Empty(AcCarUi.GetStrings(ui, "missing"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("\"text\"")]
    [InlineData("{\"a\": ")]
    [InlineData("")]
    public void Not_a_json_object_is_an_error_not_an_exception(string json)
    {
        Assert.Null(AcCarUi.TryParse(json, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void A_missing_file_is_an_error()
    {
        using var temp = new TempDir();
        Assert.Null(AcCarUi.TryRead(temp.Path, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Numbers_and_bools_read_as_invariant_text()
    {
        var ui = AcCarUi.TryParse("{\"a\": 1.5, \"b\": true}", out _);
        Assert.Equal("1.5", AcCarUi.GetString(ui, "a"));
        Assert.Equal("True", AcCarUi.GetString(ui, "b"));
    }
}

public class PathNamesTests
{
    [Theory]
    [InlineData("Bob", "Bob")]
    [InlineData("a/b", "a_b")]
    [InlineData("a<>:\"b", "a_b")]
    [InlineData("..", "unnamed")]
    [InlineData(".", "unnamed")]
    [InlineData("", "unnamed")]
    [InlineData(null, "unnamed")]
    [InlineData("   ", "unnamed")]
    [InlineData("name...", "name")]
    public void Sanitize(string? name, string expected) => Assert.Equal(expected, PathNames.Sanitize(name));

    [Fact]
    public void Sanitize_caps_the_length()
    {
        var name = PathNames.Sanitize(new string('x', 200));
        Assert.Equal(50, name.Length);
        Assert.True(PathNames.IsSafeSegment(name));
    }

    [Theory]
    [InlineData("car_a", true)]
    [InlineData("my car", true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData(null, false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("C:", false)]
    [InlineData("C:\\x", false)]
    [InlineData("\\\\server\\share", false)]
    [InlineData("a*b", false)]
    public void IsSafeSegment(string? name, bool safe) => Assert.Equal(safe, PathNames.IsSafeSegment(name));

    [Theory]
    [InlineData("...")]
    [InlineData("car.")]
    [InlineData("car ")]
    public void A_segment_windows_would_rename_never_resolves_outside_or_onto_the_root(string name)
    {
        using var temp = new TempDir();
        // Whether or not it is called safe, joining it under a root must never give the root or anything outside it
        if (PathNames.TryCombineUnder(temp.Path, name, out var full))
            Assert.True(PathNames.IsUnder(temp.Path, full));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("..\\x")]
    [InlineData("a\\..\\..\\x")]
    [InlineData("C:\\Windows")]
    [InlineData("C:x")]
    [InlineData("\\x")]
    [InlineData("\\\\server\\share\\x")]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("a\\..")]
    public void TryCombineUnder_refuses_what_leaves_the_root(string relative)
    {
        using var temp = new TempDir();
        Assert.False(PathNames.TryCombineUnder(temp.Path, relative, out var full));
        Assert.Equal(string.Empty, full);
        Assert.Throws<InvalidOperationException>(() => PathNames.CombineUnder(temp.Path, relative));
    }

    [Theory]
    [InlineData("a", "a")]
    [InlineData("a\\b.kn5", "a\\b.kn5")]
    [InlineData("a/b", "a\\b")]
    [InlineData("a\\..\\b", "b")]
    public void TryCombineUnder_accepts_what_stays_inside(string relative, string expected)
    {
        using var temp = new TempDir();
        Assert.True(PathNames.TryCombineUnder(temp.Path + "\\", relative, out var full));
        Assert.Equal(Path.Combine(temp.Path, expected), full);
    }

    [Fact]
    public void IsUnder_is_not_fooled_by_a_shared_prefix()
    {
        Assert.False(PathNames.IsUnder("C:\\cars", "C:\\cars2\\x"));
        Assert.False(PathNames.IsUnder("C:\\cars", "C:\\cars"));
        Assert.True(PathNames.IsUnder("C:\\cars\\", "C:\\CARS\\x"));
    }
}

public class SafeFileTests
{
    [Fact]
    public void Writes_overwrites_and_leaves_no_temp_file()
    {
        using var temp = new TempDir();
        var path = temp.Combine("sub", "file.json");

        SafeFile.WriteAllText(path, "first");
        Assert.Equal("first", File.ReadAllText(path));
        SafeFile.WriteAllText(path, "second é");
        Assert.Equal(Encoding.UTF8.GetBytes("second é"), File.ReadAllBytes(path)); // no BOM
        SafeFile.WriteAllBytes(path, [1, 2, 3]);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));

        Assert.Equal(["file.json"], Directory.GetFiles(temp.Combine("sub")).Select(Path.GetFileName));
    }

    [Fact]
    public void A_failed_write_leaves_the_old_file_and_no_temp_file()
    {
        using var temp = new TempDir();
        var path = temp.File("locked.txt", "old");

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.ThrowsAny<Exception>(() => SafeFile.WriteAllText(path, "new")); // access denied on the rename, as it happens

        Assert.Equal("old", File.ReadAllText(path));
        Assert.Equal(["locked.txt"], Directory.GetFiles(temp.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void An_encoding_is_honoured()
    {
        using var temp = new TempDir();
        var path = temp.Combine("latin.ini");
        SafeFile.WriteAllText(path, "é", Encoding.Latin1);
        Assert.Equal(new byte[] { 0xE9 }, File.ReadAllBytes(path));
    }
}

public class CarValuationTests
{
    [Theory]
    [InlineData(0.0, 0.5)]
    [InlineData(1.0, 1.1)]
    [InlineData(0.5, 0.8)]
    [InlineData(-3.0, 0.5)]
    [InlineData(7.0, 1.1)]
    [InlineData(double.NaN, 0.5)]
    [InlineData(double.PositiveInfinity, 0.5)]
    public void ConditionFactor(double condition, double expected) =>
        Assert.Equal((decimal)expected, CarValuation.ConditionFactor(condition));

    [Theory]
    [InlineData(900, 1000, 0)]
    [InlineData(2000, 1000, 500)]
    [InlineData(double.NaN, 1000, 0)]
    [InlineData(double.PositiveInfinity, 0, 0)]
    [InlineData(1e300, 0, 1e9)]
    public void ModificationsValue(double engine, double stock, double expected) =>
        Assert.Equal((decimal)expected, CarValuation.ModificationsValue(engine, stock));

    [Fact]
    public void Value_rounds_to_the_hundred_and_never_goes_below_zero()
    {
        Assert.Equal(16000m, CarValuation.Value(20000m, 0.5));
        Assert.Equal(16500m, CarValuation.Value(20000m, 0.5, 500m));
        Assert.Equal(0m, CarValuation.Value(-5000m, 1));
        Assert.Equal(1100m, CarValuation.Value(1000m, 1));
    }

    [Fact]
    public void ValueOf_a_car_uses_its_average_condition()
    {
        var car = new Car("x") { EngineHealth = 1, TransmissionHealth = 0, BodyCondition = 0.5, TireCondition = 0.5 };
        Assert.Equal(0.5, CarValuation.ConditionOf(car));
        Assert.Equal(16000m, CarValuation.ValueOf(car, 20000m));
    }
}

public class MatchupCalculatorTests
{
    private static CarDefinition CarWith(string? bhp, string? weight) => new() { Specs = new CarSpecsData { Bhp = bhp, Weight = weight } };

    [Fact]
    public void Compare_reads_the_specs_through_AcSpecs()
    {
        var stats = MatchupCalculator.Compare(CarWith("350hp @ 6000rpm", "1,500kg"), CarWith("300 bhp", "1.250 kg"));
        var hp = stats.Single(s => s.Label == "Horsepower");
        Assert.Equal(350.ToString("N0") + " HP", hp.PlayerValue);
        Assert.Equal(1, hp.Advantage);
        var weight = stats.Single(s => s.Label == "Weight");
        Assert.Equal(-1, weight.Advantage);
        Assert.Contains(stats, s => s.Label == "HP/Ton");
    }

    [Fact]
    public void A_stat_neither_car_states_is_left_out()
    {
        Assert.Empty(MatchupCalculator.Compare(CarWith(null, "abc"), CarWith("", null)));
        Assert.Empty(MatchupCalculator.Compare(new CarDefinition(), new CarDefinition()));
    }

    [Theory]
    [InlineData(RaceType.DragRace, 1000, 40, 10, 40)]
    [InlineData(RaceType.DragRace, 1000, 1000, 10, 100)]
    [InlineData(RaceType.Circuit, 5, 1000, 5, 5)]
    [InlineData(RaceType.Circuit, 1000, 1000, 25, 250)]
    [InlineData(RaceType.DragRace, 0, 1000, 0, 0)]
    public void WagerLimits(RaceType type, int player, int opponent, int min, int max) =>
        Assert.Equal(((decimal)min, (decimal)max), MatchupCalculator.WagerLimits(type, player, opponent));

    [Fact]
    public void A_negative_balance_gives_a_range_that_is_not_upside_down()
    {
        var (min, max) = MatchupCalculator.WagerLimits(RaceType.DragRace, -50m, 1000m);
        Assert.True(min <= max);
    }

    [Theory]
    [InlineData(30, 10, OpponentDifficulty.Hard)]
    [InlineData(10, 30, OpponentDifficulty.Easy)]
    [InlineData(20, 10, OpponentDifficulty.Matched)]
    [InlineData(25, 10, OpponentDifficulty.Matched)]
    [InlineData(26, 10, OpponentDifficulty.Hard)]
    public void DifficultyOf(int opponent, int player, OpponentDifficulty expected) =>
        Assert.Equal(expected, MatchupCalculator.DifficultyOf(opponent, player));
}

public class AcdFileTests
{
    /// <summary>A data.acd in a folder named after the car, entries encoded with that car's key</summary>
    private static string Acd(TempDir temp, string carId, params (string Name, byte[] Content)[] entries)
    {
        var key = Encoding.ASCII.GetBytes(AcdFile.KeyFor(carId));
        using var data = new MemoryStream();
        var w = new BinaryWriter(data);
        w.Write(-1111);
        w.Write(0);
        foreach (var (name, content) in entries)
        {
            var nameBytes = Encoding.Latin1.GetBytes(name);
            w.Write(nameBytes.Length);
            w.Write(nameBytes);
            w.Write(content.Length);
            for (var i = 0; i < content.Length; i++) w.Write((int)(byte)(content[i] + key[i % key.Length]));
        }

        return temp.Bytes(Path.Combine(carId, AcdFile.FileName), data.ToArray());
    }

    [Fact]
    public void Entries_decode_and_one_can_be_read_alone()
    {
        using var temp = new TempDir();
        var path = Acd(temp, "sr_test_car", ("engine.ini", Encoding.ASCII.GetBytes("[HEADER]\r\nVERSION=1")), ("car.ini", [0, 255, 128]));

        var entries = AcdFile.Read(path);
        Assert.Equal(["engine.ini", "car.ini"], entries.Select(e => e.Name));
        Assert.Equal("[HEADER]\r\nVERSION=1", Encoding.ASCII.GetString(entries[0].Content));
        Assert.Equal(new byte[] { 0, 255, 128 }, AcdFile.ReadEntry(path, "CAR.INI"));
        Assert.Null(AcdFile.ReadEntry(path, "missing.ini"));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(int.MaxValue, 0)]
    [InlineData(4, -1)]
    [InlineData(4, int.MaxValue)]
    [InlineData(4, 1_000_000)]
    public void A_length_that_is_negative_or_past_the_end_is_invalid_data(int nameLength, int size)
    {
        using var temp = new TempDir();
        using var data = new MemoryStream();
        var w = new BinaryWriter(data);
        w.Write(-1111);
        w.Write(0);
        w.Write(nameLength);
        w.Write(Encoding.ASCII.GetBytes("a.in"));
        w.Write(size);
        w.Write(new byte[16]);
        var path = temp.Bytes(Path.Combine("car_x", AcdFile.FileName), data.ToArray());

        Assert.Throws<InvalidDataException>(() => AcdFile.Read(path));
        Assert.Throws<InvalidDataException>(() => AcdFile.ReadEntry(path, "a.in"));
    }

    [Fact]
    public void A_file_cut_after_the_name_is_invalid_data()
    {
        using var temp = new TempDir();
        var bytes = File.ReadAllBytes(Acd(temp, "car_y", ("engine.ini", [1, 2, 3])));
        var path = temp.Bytes(Path.Combine("car_y", "cut", AcdFile.FileName), bytes[..(8 + 4 + 10 + 2)]);
        Assert.Throws<InvalidDataException>(() => AcdFile.Read(path));
    }
}

public class PartPricingTests
{
    private static PartDefinition PartWorth(object? value) =>
        new() { Id = "p", Properties = value == null ? new() : new() { ["value"] = value } };

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-10.0)]
    [InlineData(0.0)]
    public void A_price_that_is_no_number_is_the_default(double value) =>
        Assert.Equal(PartPricing.NewPrice(PartWorth(null)), PartPricing.NewPrice(PartWorth(value)));

    [Fact]
    public void A_real_price_is_used()
    {
        Assert.Equal(250.0 * PartPricing.Scale, PartPricing.NewPrice(PartWorth(250.0)));
        Assert.Equal(250.0 * PartPricing.Scale, PartPricing.NewPrice(PartWorth(250L)));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(1e300)]
    [InlineData(-5.0)]
    public void Round_never_throws(double price)
    {
        var rounded = PartPricing.Round(price);
        Assert.InRange(rounded, 0m, 1e12m);
    }

    [Fact]
    public void Worth_with_nonsense_wear_stays_a_number()
    {
        var part = PartWorth(100.0);
        var worth = PartPricing.Worth(part, new PartInstance { Tear = double.NaN, Wear = double.PositiveInfinity });
        Assert.InRange(PartPricing.Round(worth), 0m, 1e12m);
        Assert.Equal(PartPricing.NewPrice(part), PartPricing.Worth(part, new PartInstance { Tear = 5, Wear = 5 }));
    }
}
