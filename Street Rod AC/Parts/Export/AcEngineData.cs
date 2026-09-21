using System.Globalization;
using System.Text;
using Street_Rod_AC.Parts.Logic;

namespace Street_Rod_AC.Parts.Export;

public sealed class AcEngineDataOptions
{
    /// <summary>
    /// Share of the engine's torque that reaches the wheels. Assetto Corsa has no drivetrain loss of its own,
    /// so car authors put it into the power curve; the dyno figures are at the flywheel.
    /// </summary>
    public double DrivetrainEfficiency { get; set; } = 0.87;

    /// <summary>The part scripts' inertia figures (mass * 0.09, summed) to kg m2 of a spinning engine</summary>
    public double InertiaScale { get; set; } = 0.02;

    /// <summary>Whether the build's transmission replaces the car's gears, final drive and differential lock</summary>
    public bool ReplaceGearbox { get; set; } = true;
}

/// <summary>
/// An engine build as Assetto Corsa physics data: the files of a car's data folder that the engine and the
/// transmission decide (power.lut, engine.ini, drivetrain.ini, ai.ini, setup.ini), made by changing the car's own
/// files as little as possible. Nothing is written here; the caller decides where the files go.
/// </summary>
public static class AcEngineData
{
    private const string EngineFile = "engine.ini";
    private const string DrivetrainFile = "drivetrain.ini";
    private const string AiFile = "ai.ini";
    private const string SetupFile = "setup.ini";
    private const string DefaultPowerCurve = "power.lut";

    private const double LutStep = 250;
    private const int MaxTurbos = 8;
    private const int MaxGears = 10;

    /// <param name="readFile">Text of a file of the car's data (folder or data.acd), null when it has no such file</param>
    /// <returns>File name to new content, for the files that change</returns>
    public static Dictionary<string, string> Generate(EngineReport report, Func<string, string?> readFile, AcEngineDataOptions? options = null)
    {
        if (report.Dyno is not { Curve.Count: > 0 } dyno)
            throw new InvalidOperationException("The engine has no power curve: " + (report.Problem ?? "it was not evaluated"));

        options ??= new AcEngineDataOptions();
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var engine = new IniText(readFile(EngineFile));
        var curveFile = engine.Get("HEADER", "POWER_CURVE") is { Length: > 0 } existing ? existing : DefaultPowerCurve;

        files[curveFile] = PowerCurve(dyno, options.DrivetrainEfficiency);
        files[EngineFile] = Engine(engine, report, dyno, curveFile, options);
        files[DrivetrainFile] = Drivetrain(new IniText(readFile(DrivetrainFile)), report, dyno, options);

        if (readFile(AiFile) is { } ai) files[AiFile] = Ai(new IniText(ai), report, dyno);
        if (options.ReplaceGearbox && report.GearRatios.Count > 0 && readFile(SetupFile) is { } setup) files[SetupFile] = Setup(new IniText(setup));

        return files;
    }

    private static string PowerCurve(DynoResult dyno, double efficiency)
    {
        var text = new StringBuilder();
        var last = dyno.Curve[^1].Rpm;
        for (var rpm = 0.0; rpm <= last; rpm += LutStep)
        {
            // An engine does not make its running torque at standstill; the curve leads in from half of the first point
            var torque = rpm == 0 ? dyno.Curve[0].Torque * 0.5 : dyno.TorqueAt(rpm);
            text.Append(Number(rpm, "0")).Append('|').Append(Number(torque * efficiency, "0.0")).Append("\r\n");
        }

        return text.ToString();
    }

    private static string Engine(IniText ini, EngineReport report, DynoResult dyno, string curveFile, AcEngineDataOptions options)
    {
        ini.Set("HEADER", "POWER_CURVE", curveFile);
        if (ini.Get("HEADER", "COAST_CURVE") == null) ini.Set("HEADER", "COAST_CURVE", "FROM_COAST_REF");

        ini.Set("ENGINE_DATA", "INERTIA", Number(Math.Clamp(report.Inertia * options.InertiaScale, 0.05, 0.6), "0.000"));
        ini.Set("ENGINE_DATA", "LIMITER", Number(report.LimiterRpm, "0"));
        ini.Set("ENGINE_DATA", "MINIMUM", Number(report.IdleRpm, "0"));
        if (ini.Get("ENGINE_DATA", "LIMITER_HZ") == null) ini.Set("ENGINE_DATA", "LIMITER_HZ", "30");
        if (ini.Get("ENGINE_DATA", "ALTITUDE_SENSITIVITY") == null) ini.Set("ENGINE_DATA", "ALTITUDE_SENSITIVITY", "0.1");

        // Engine braking grows with size: about 12 Nm per litre at the limiter
        ini.Set("COAST_REF", "RPM", Number(report.LimiterRpm, "0"));
        ini.Set("COAST_REF", "TORQUE", Number(dyno.Displacement * 1000 * 12, "0"));
        if (ini.Get("COAST_REF", "NON_LINEARITY") == null) ini.Set("COAST_REF", "NON_LINEARITY", "0");

        // The power curve already has the charger in it; the car's own turbos would come on top
        for (var i = 0; i < MaxTurbos; i++) ini.RemoveSection($"TURBO_{i}");

        // What the weakest rotating part of the build survives is where the engine starts to hurt
        var weakest = dyno.Curve[^1].Rpm;
        ini.Set("DAMAGE", "RPM_THRESHOLD", Number(Math.Max(weakest, report.IdleRpm + 1000), "0"));
        if (ini.Get("DAMAGE", "RPM_DAMAGE_K") == null) ini.Set("DAMAGE", "RPM_DAMAGE_K", "1");

        return ini.ToString();
    }

    private static string Drivetrain(IniText ini, EngineReport report, DynoResult dyno, AcEngineDataOptions options)
    {
        if (options.ReplaceGearbox && report.GearRatios.Count > 0)
        {
            ini.Set("GEARS", "COUNT", report.GearRatios.Count.ToString(CultureInfo.InvariantCulture));
            ini.Set("GEARS", "GEAR_R", Number(-Math.Abs(report.ReverseRatio > 0 ? report.ReverseRatio : report.GearRatios[0]), "0.000"));
            for (var i = 0; i < MaxGears; i++)
            {
                if (i < report.GearRatios.Count) ini.Set("GEARS", $"GEAR_{i + 1}", Number(report.GearRatios[i], "0.000"));
                else ini.RemoveKey("GEARS", $"GEAR_{i + 1}");
            }

            if (report.FinalRatio > 0) ini.Set("GEARS", "FINAL", Number(report.FinalRatio, "0.000"));

            ini.Set("DIFFERENTIAL", "POWER", Number(Math.Clamp(report.DiffLock, 0, 1), "0.00"));
            ini.Set("DIFFERENTIAL", "COAST", Number(Math.Clamp(report.DiffLock, 0, 1) * 0.75, "0.00"));
        }

        // A clutch that holds the new engine
        var needed = dyno.MaxTorque * 1.3;
        var current = double.TryParse(ini.Get("CLUTCH", "MAX_TORQUE"), NumberStyles.Float, CultureInfo.InvariantCulture, out var torque) ? torque : 0;
        if (current < needed) ini.Set("CLUTCH", "MAX_TORQUE", Number(needed, "0"));

        return ini.ToString();
    }

    /// <summary>Opponents and the automatic gearbox shift by these</summary>
    private static string Ai(IniText ini, EngineReport report, DynoResult dyno)
    {
        var up = Math.Min(report.LimiterRpm * 0.97, dyno.MaxPowerRpm + 400);
        ini.Set("GEARS", "UP", Number(up, "0"));
        ini.Set("GEARS", "DOWN", Number(Math.Max(report.IdleRpm + 800, up * 0.6), "0"));
        return ini.ToString();
    }

    /// <summary>Ratio choices of the car's setup screen would override the transmission's gears</summary>
    private static string Setup(IniText ini)
    {
        foreach (var section in ini.Sections.Where(s => s.StartsWith("GEAR_", StringComparison.Ordinal) || s == "FINAL_GEAR_RATIO").ToList())
            ini.RemoveSection(section);
        return ini.ToString();
    }

    private static string Number(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
}
