using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Parts.Export;
using Street_Rod_AC.Parts.Logic;

namespace StreetRodAC.Tests;

/// <summary>What an engine build and the running gear make of a small car's data, read from a temp folder</summary>
public sealed class AcDataExportTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private const string Drivetrain = """
        [HEADER]
        VERSION=3
        [TRACTION]
        TYPE=RWD
        [GEARS]
        COUNT=3
        GEAR_R=-2.500
        GEAR_1=2.500
        GEAR_2=1.500
        GEAR_3=1.000
        FINAL=3.550
        [CLUTCH]
        MAX_TORQUE=300
        [AUTO_SHIFTER]
        UP=6500
        DOWN=3000
        """;

    private static string Engine(string powerCurve) => $"""
        [HEADER]
        VERSION=1
        POWER_CURVE={powerCurve}
        COAST_CURVE=FROM_COAST_REF
        [ENGINE_DATA]
        ALTITUDE_SENSITIVITY=0.1
        INERTIA=0.120
        LIMITER=6500
        MINIMUM=900
        """;

    /// <summary>A small V8 that peaks at 400 Nm, no gearbox of its own</summary>
    private static EngineReport Report()
    {
        var curve = new List<(double Rpm, double Torque)>();
        for (var rpm = 1000.0; rpm <= 6000; rpm += 500) curve.Add((rpm, 400 - Math.Abs(rpm - 3500) * 0.05));
        return new EngineReport { Dyno = new DynoResult(0.0057, 9.5, curve), IdleRpm = 800, LimiterRpm = 5800, Inertia = 8, Mass = 250 };
    }

    private Func<string, string?> CarData(string powerCurve)
    {
        _temp.File("data/engine.ini", Engine(powerCurve));
        _temp.File("data/drivetrain.ini", Drivetrain);
        return AcCarDataReader.ForFolder(_temp.Combine("data"));
    }

    [Fact]
    public void The_engine_data_follows_the_dyno_and_keeps_the_cars_curve_file()
    {
        var files = AcEngineData.Generate(Report(), CarData("power_v8.lut"));

        Assert.Equal(["drivetrain.ini", "engine.ini", "power_v8.lut"], files.Keys.Order(StringComparer.Ordinal));

        var engine = new IniText(files["engine.ini"]);
        Assert.Equal("power_v8.lut", engine.Get("HEADER", "POWER_CURVE"));
        Assert.Equal(5800, engine.GetNumber("ENGINE_DATA", "LIMITER"));
        Assert.Equal(800, engine.GetNumber("ENGINE_DATA", "MINIMUM"));

        // The curve is the dyno's, every 250 rpm up to its last point, leading in from half the first
        var lut = files["power_v8.lut"].Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("0|137.5", lut[0]);
        Assert.Equal("3500|400.0", lut.Single(l => l.StartsWith("3500|", StringComparison.Ordinal)));
        Assert.Equal("6000|275.0", lut[^1]);

        // The car's automatic shifts inside the new limiter
        var drivetrain = new IniText(files["drivetrain.ini"]);
        Assert.True(drivetrain.GetNumber("AUTO_SHIFTER", "UP") <= 5800);
    }

    [Theory]
    [InlineData(@"..\..\x.lut")]
    [InlineData("../x.lut")]
    [InlineData(@"C:\x.lut")]
    [InlineData("engine.ini")]
    [InlineData("car.ini")]
    public void A_curve_file_name_that_leaves_the_folder_or_is_no_lut_becomes_power_lut(string powerCurve)
    {
        var files = AcEngineData.Generate(Report(), CarData(powerCurve));

        Assert.Contains("power.lut", files.Keys);
        // The curve went nowhere else, over none of the .ini files
        Assert.Equal(["drivetrain.ini", "engine.ini", "power.lut"], files.Keys.Order(StringComparer.Ordinal));
        Assert.StartsWith("[HEADER]", files["engine.ini"]);
        Assert.All(files.Keys, name => Assert.True(Street_Rod_AC.Helpers.PathNames.IsSafeSegment(name), name));
        Assert.Equal("power.lut", new IniText(files["engine.ini"]).Get("HEADER", "POWER_CURVE"));
        Assert.StartsWith("0|", files["power.lut"]);
    }

    // ---- Running gear ----

    private const string Tyres = """
        [HEADER]
        VERSION=10
        [FRONT]
        NAME=Street
        WIDTH=0.205
        RADIUS=0.3200
        DX_REF=1.2000
        DY_REF=1.1000
        [REAR]
        NAME=Street
        WIDTH=0.205
        RADIUS=0.3200
        DX_REF=1.2000
        DY_REF=1.1000
        [THERMAL_FRONT]
        SURFACE_TRANSFER=0.0200
        """;

    private const string Brakes = """
        [HEADER]
        VERSION=1
        [DATA]
        MAX_TORQUE=1000
        FRONT_SHARE=0.600
        HANDBRAKE_TORQUE=400
        """;

    /// <summary>Two tyres, two brakes, a rim, a spring and a shock; the sticky tyre grips a fifth more, the big brake twice as hard</summary>
    private PartsCatalog GearCatalog()
    {
        _temp.File("parts/gear/test/pack.json", """
            {
              "id": "gear/test",
              "parts": [
                { "id": "gear/test/rim", "mass": 8, "properties": { "offset": 0.0, "wheel_radius": 0.1905 } },
                { "id": "gear/test/tyre", "mass": 10, "properties": { "friction": 1.0, "radius": 0.32, "tyre_width": 205, "wheel_radius": 190.5 } },
                { "id": "gear/test/sticky", "mass": 10, "properties": { "friction": 1.2, "radius": 0.32, "tyre_width": 205, "wheel_radius": 190.5 } },
                { "id": "gear/test/brake", "mass": 5, "properties": { "force": 1000, "radius": 0.1 } },
                { "id": "gear/test/bigbrake", "mass": 5, "properties": { "force": 2000, "radius": 0.1 } },
                { "id": "gear/test/spring", "mass": 2, "properties": { "force": 30000 } },
                { "id": "gear/test/shock", "mass": 2 }
              ]
            }
            """);
        return PartsCatalog.Load(_temp.Combine("parts"));
    }

    private static (RunningGearFactory.AxleParts, RunningGearFactory.AxleParts) Factory(PartsCatalog catalog)
    {
        var axle = new RunningGearFactory.AxleParts(catalog.Get("gear/test/tyre")!, catalog.Get("gear/test/rim")!,
            catalog.Get("gear/test/brake")!, catalog.Get("gear/test/spring")!, catalog.Get("gear/test/shock")!);
        return (axle, axle);
    }

    private static CornerParts[] Corners(string tyre, string frontBrake) =>
        Enumerable.Range(0, RunningGear.Corners).Select(i => new CornerParts
        {
            Rim = new PartInstance("gear/test/rim"),
            Tyre = new PartInstance(tyre),
            Brake = new PartInstance(RunningGear.IsFront(i) ? frontBrake : "gear/test/brake"),
            Spring = new PartInstance("gear/test/spring"),
            Shock = new PartInstance("gear/test/shock")
        }).ToArray();

    private Func<string, string?> GearData()
    {
        _temp.File("data/tyres.ini", Tyres);
        _temp.File("data/brakes.ini", Brakes);
        return AcCarDataReader.ForFolder(_temp.Combine("data"));
    }

    [Fact]
    public void A_car_on_its_factory_running_gear_changes_nothing()
    {
        var catalog = GearCatalog();
        var problems = new List<string>();

        var files = AcRunningGearData.Generate(catalog, Corners("gear/test/tyre", "gear/test/brake"), Factory(catalog), GearData(), problems);

        Assert.Empty(files);
        Assert.Empty(problems);
    }

    [Fact]
    public void Stickier_tyres_and_bigger_front_brakes_move_the_cars_figures_by_their_ratio()
    {
        var catalog = GearCatalog();
        var problems = new List<string>();

        var files = AcRunningGearData.Generate(catalog, Corners("gear/test/sticky", "gear/test/bigbrake"), Factory(catalog), GearData(), problems);

        Assert.Empty(problems);
        var tyres = new IniText(files["tyres.ini"]);
        Assert.Equal(1.44, tyres.GetNumber("FRONT", "DX_REF")!.Value, 4);
        Assert.Equal(1.32, tyres.GetNumber("REAR", "DY_REF")!.Value, 4);
        Assert.Equal(0.205, tyres.GetNumber("FRONT", "WIDTH")!.Value, 4);

        // Front 600 Nm doubled, rear 400 as it was
        var brakes = new IniText(files["brakes.ini"]);
        Assert.Equal(1600, brakes.GetNumber("DATA", "MAX_TORQUE"));
        Assert.Equal(0.75, brakes.GetNumber("DATA", "FRONT_SHARE")!.Value, 3);
        Assert.Equal(400, brakes.GetNumber("DATA", "HANDBRAKE_TORQUE"));
    }

    [Fact]
    public void A_corner_without_a_brake_is_a_problem_and_counts_as_the_factory_one()
    {
        var catalog = GearCatalog();
        var problems = new List<string>();
        var corners = Corners("gear/test/tyre", "gear/test/brake");
        corners[3].Brake = null;

        var files = AcRunningGearData.Generate(catalog, corners, Factory(catalog), GearData(), problems);

        Assert.Equal(["no brake rear right"], problems);
        Assert.DoesNotContain("brakes.ini", files.Keys);
    }
}
