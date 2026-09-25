using System.Globalization;
using System.IO;
using System.Text;
using Street_Rod_AC.Helpers;
using Street_Rod_AC.Parts.Logic;

namespace Street_Rod_AC.Parts.Export;

public sealed class AcEngineDataOptions
{
    /// <summary>
    /// Share of the engine's torque that goes into the curve. Assetto Corsa has no drivetrain loss of its own,
    /// so car authors sometimes put it into the power curve; the game's rule is that the file says what the
    /// dyno says, so the whole flywheel figure goes in.
    /// </summary>
    public double DrivetrainEfficiency { get; set; } = 1.0;

    /// <summary>The part scripts' inertia figures (mass * 0.09, summed) to kg m2 of a spinning engine</summary>
    public double InertiaScale { get; set; } = 0.02;

    /// <summary>Whether the build's transmission replaces the car's gears, final drive and differential lock</summary>
    public bool ReplaceGearbox { get; set; } = true;

    /// <summary>
    /// Kilograms of the engine the car's data was made with. Given, the car's mass moves by what the build
    /// weighs more or less than that; null leaves the mass alone.
    /// </summary>
    public double? FactoryEngineMass { get; set; }

    /// <summary>
    /// Nm the clutch holds per unit of the scripts' clamp figure. Set so that every factory engine's clutch
    /// holds it (the big-block packs put a 300 behind 770 Nm); a built engine outgrows its stock clutch.
    /// </summary>
    public double ClutchTorquePerUnit { get; set; } = 2.6;

    /// <summary>Engine braking of a stock oil pan, Nm per litre at the limiter</summary>
    public double CoastTorquePerLitre { get; set; } = 12;

    /// <summary>The scripts' friction figure of a stock oil pan on a stock bottom end (the median of the builds); more drag means more braking</summary>
    public double ReferenceFriction { get; set; } = 0.0002;
}

/// <summary>
/// An engine build as Assetto Corsa physics data: the files of a car's data folder that the engine and the
/// transmission decide (power.lut, engine.ini, drivetrain.ini, ai.ini, setup.ini, and car.ini for the mass),
/// made by changing the car's own files as little as possible. Nothing is written here; the caller decides
/// where the files go.
/// </summary>
public static class AcEngineData
{
    private const string EngineFile = "engine.ini";
    private const string DrivetrainFile = "drivetrain.ini";
    private const string AiFile = "ai.ini";
    private const string SetupFile = "setup.ini";
    private const string CarFile = "car.ini";
    private const string DefaultPowerCurve = "power.lut";

    private const double LutStep = 250;
    private const int MaxTurbos = 8;
    private const int MaxGears = 10;

    // A downshift point this far below where the widest upshift lands keeps the gearbox from hunting
    private const double DownshiftMargin = 0.9;

    /// <param name="readFile">Text of a file of the car's data (folder or data.acd), null when it has no such file</param>
    /// <returns>File name to new content, for the files that change</returns>
    /// <exception cref="FileNotFoundException">The car has no engine.ini or drivetrain.ini: not a car's data</exception>
    public static Dictionary<string, string> Generate(EngineReport report, Func<string, string?> readFile, AcEngineDataOptions? options = null)
    {
        if (report.Dyno is not { Curve.Count: > 0 } dyno)
            throw new InvalidOperationException("The engine has no power curve: " + (report.Problem ?? "it was not evaluated"));

        options ??= new AcEngineDataOptions();
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Without the car's own files there is nothing to change, and a file of our few keys alone is not a car
        var engine = new IniText(readFile(EngineFile) ?? throw new FileNotFoundException("The car's data has no " + EngineFile, EngineFile));
        var drivetrain = new IniText(readFile(DrivetrainFile) ?? throw new FileNotFoundException("The car's data has no " + DrivetrainFile, DrivetrainFile));

        // A build without a limiter revs as far as its curve goes
        var limiter = report.LimiterRpm > 0 ? report.LimiterRpm : dyno.Curve[^1].Rpm;
        // The car's own curve file keeps its name, if it is a plain .lut name: it becomes a file the caller writes,
        // so a mod's engine.ini must not send it out of the data folder or over one of the .ini files
        var curveFile = engine.Get("HEADER", "POWER_CURVE") is { Length: > 0 } existing && PathNames.IsSafeSegment(existing)
            && existing.EndsWith(".lut", StringComparison.OrdinalIgnoreCase) ? existing : DefaultPowerCurve;

        var turbo = Turbo(report, dyno);
        files[curveFile] = PowerCurve(dyno, options.DrivetrainEfficiency, turbo);
        files[EngineFile] = Engine(engine, report, dyno, limiter, curveFile, turbo, options);

        GearsAndClutch(drivetrain, report, dyno, options);
        var (up, down) = ShiftPoints(drivetrain, report, dyno, limiter);
        AutoShifter(drivetrain, up, down);
        files[DrivetrainFile] = drivetrain.ToString();

        if (readFile(AiFile) is { } ai) files[AiFile] = Ai(new IniText(ai), up, down);
        if (options.ReplaceGearbox && report.GearRatios.Count > 0 && readFile(SetupFile) is { } setup && Setup(new IniText(setup)) is { Changed: true } trimmed)
            files[SetupFile] = trimmed.ToString();
        // The car weighs what it did plus what the engine weighs more than the one it came with
        if (options.FactoryEngineMass is { } factoryMass && report.Mass > 0 && readFile(CarFile) is { } car)
        {
            var ini = new IniText(car);
            AcCarIni.AddMass(ini, report.Mass - factoryMass);
            if (ini.Changed) files[CarFile] = ini.ToString();
        }

        return files;
    }

    /// <summary>
    /// The torque the game will see at each speed with the throttle open: the dyno's, exactly. With a turbo
    /// the game multiplies the curve by its own boost, so the curve is the dyno's divided by that.
    /// </summary>
    private static string PowerCurve(DynoResult dyno, double efficiency, AcTurbo? turbo)
    {
        var text = new StringBuilder();
        var last = dyno.Curve[^1].Rpm;
        for (var rpm = 0.0; rpm <= last; rpm += LutStep)
        {
            // An engine does not make its running torque at standstill; the curve leads in from half of the first point
            var torque = rpm == 0 ? dyno.Curve[0].Torque * 0.5 : dyno.TorqueAt(rpm);
            if (turbo != null) torque /= 1 + turbo.BoostAt(rpm);
            text.Append(Number(rpm, "0")).Append('|').Append(Number(torque * efficiency, "0.0")).Append("\r\n");
        }

        return text.ToString();
    }

    private static string Engine(IniText ini, EngineReport report, DynoResult dyno, double limiter, string curveFile, AcTurbo? turbo, AcEngineDataOptions options)
    {
        ini.Set("HEADER", "POWER_CURVE", curveFile);
        if (ini.Get("HEADER", "COAST_CURVE") == null) ini.Set("HEADER", "COAST_CURVE", "FROM_COAST_REF");

        ini.Set("ENGINE_DATA", "INERTIA", Math.Clamp(report.Inertia * options.InertiaScale, 0.05, 0.6), "0.000");
        ini.Set("ENGINE_DATA", "LIMITER", limiter, "0");
        ini.Set("ENGINE_DATA", "MINIMUM", report.IdleRpm, "0");
        if (ini.Get("ENGINE_DATA", "LIMITER_HZ") == null) ini.Set("ENGINE_DATA", "LIMITER_HZ", "30");
        if (ini.Get("ENGINE_DATA", "ALTITUDE_SENSITIVITY") == null) ini.Set("ENGINE_DATA", "ALTITUDE_SENSITIVITY", "0.1");

        // Engine braking grows with size, about 12 Nm per litre at the limiter, and with the drag of the oil
        // pan and bottom end the build has (a racing pan and low-friction parts let the engine spin freer)
        var friction = report.Friction > 0 && options.ReferenceFriction > 0 ? Math.Clamp(report.Friction / options.ReferenceFriction, 0.5, 2) : 1;
        ini.Set("COAST_REF", "RPM", limiter, "0");
        ini.Set("COAST_REF", "TORQUE", dyno.Displacement * 1000 * options.CoastTorquePerLitre * friction, "0");
        if (ini.Get("COAST_REF", "NON_LINEARITY") == null) ini.Set("COAST_REF", "NON_LINEARITY", "0");

        // The car's own turbos would come on top of the curve; a turbo of the build gets the game's turbo model
        for (var i = 0; i < MaxTurbos; i++) ini.RemoveSection($"TURBO_{i}");
        if (turbo != null)
        {
            ini.Set("TURBO_0", "LAG_DN", "0.985");
            ini.Set("TURBO_0", "LAG_UP", "0.992");
            ini.Set("TURBO_0", "MAX_BOOST", turbo.MaxBoost, "0.000");
            ini.Set("TURBO_0", "WASTEGATE", turbo.MaxBoost, "0.000");
            ini.Set("TURBO_0", "DISPLAY_MAX_BOOST", turbo.MaxBoost, "0.00");
            ini.Set("TURBO_0", "REFERENCE_RPM", turbo.ReferenceRpm, "0");
            ini.Set("TURBO_0", "GAMMA", turbo.Gamma, "0.00");
            ini.Set("TURBO_0", "COCKPIT_ADJUSTABLE", "0");
            ini.Set("DAMAGE", "TURBO_BOOST_THRESHOLD", turbo.MaxBoost + 0.5, "0.00");
            if (ini.Get("DAMAGE", "TURBO_DAMAGE_K") == null) ini.Set("DAMAGE", "TURBO_DAMAGE_K", "5");
        }

        // What the weakest rotating part of the build survives is where the engine starts to hurt
        var weakest = dyno.Curve[^1].Rpm;
        ini.Set("DAMAGE", "RPM_THRESHOLD", Math.Max(weakest, report.IdleRpm + 1000), "0");
        if (ini.Get("DAMAGE", "RPM_DAMAGE_K") == null) ini.Set("DAMAGE", "RPM_DAMAGE_K", "1");

        return ini.ToString();
    }

    private static void GearsAndClutch(IniText ini, EngineReport report, DynoResult dyno, AcEngineDataOptions options)
    {
        if (options.ReplaceGearbox && report.GearRatios.Count > 0)
        {
            ini.Set("GEARS", "COUNT", report.GearRatios.Count.ToString(CultureInfo.InvariantCulture));
            ini.Set("GEARS", "GEAR_R", -Math.Abs(report.ReverseRatio > 0 ? report.ReverseRatio : report.GearRatios[0]), "0.000");
            for (var i = 0; i < MaxGears; i++)
            {
                if (i < report.GearRatios.Count) ini.Set("GEARS", $"GEAR_{i + 1}", report.GearRatios[i], "0.000");
                else ini.RemoveKey("GEARS", $"GEAR_{i + 1}");
            }

            if (report.FinalRatio > 0) ini.Set("GEARS", "FINAL", report.FinalRatio, "0.000");

            ini.Set("DIFFERENTIAL", "POWER", Math.Clamp(report.DiffLock, 0, 1), "0.00");
            ini.Set("DIFFERENTIAL", "COAST", Math.Clamp(report.DiffLock, 0, 1) * 0.75, "0.00");
        }

        // The clutch of the build holds what its clamp is good for: a stock clutch behind a built engine slips.
        // Without a clutch part the car's own is raised to hold the engine.
        if (report.ClutchCapacity > 0)
        {
            ini.Set("CLUTCH", "MAX_TORQUE", report.ClutchCapacity * options.ClutchTorquePerUnit, "0");
        }
        else
        {
            var needed = dyno.MaxTorque * 1.3;
            var current = ini.GetNumber("CLUTCH", "MAX_TORQUE") ?? 0;
            if (current < needed) ini.Set("CLUTCH", "MAX_TORQUE", needed, "0");
        }
    }

    /// <summary>
    /// Where to shift, by the gears the car ends up with. An upshift drops the revs by the step between the two
    /// gears; a downshift point above where the widest step lands would shift straight back down, and up again.
    /// </summary>
    private static (double Up, double Down) ShiftPoints(IniText drivetrain, EngineReport report, DynoResult dyno, double limiter)
    {
        var up = Math.Min(limiter * 0.97, dyno.MaxPowerRpm + 400);
        var down = Math.Max(report.IdleRpm + 800, up * 0.6);

        var ratios = Enumerable.Range(1, MaxGears)
            .Select(i => drivetrain.GetNumber("GEARS", $"GEAR_{i}") ?? 0)
            .TakeWhile(r => r > 0)
            .ToList();
        if (ratios.Count > 1)
        {
            var widestStep = ratios.Zip(ratios.Skip(1), (lower, higher) => higher / lower).Min();
            down = Math.Min(down, up * widestStep * DownshiftMargin);
        }

        return (up, down);
    }

    /// <summary>The car's own automatic gearbox has shift points too; left at the old engine's they may lie beyond the limiter</summary>
    private static void AutoShifter(IniText ini, double up, double down)
    {
        if (ini.Get("AUTO_SHIFTER", "UP") != null) ini.Set("AUTO_SHIFTER", "UP", up, "0");
        if (ini.Get("AUTO_SHIFTER", "DOWN") != null) ini.Set("AUTO_SHIFTER", "DOWN", down, "0");
    }

    /// <summary>Opponents shift by these</summary>
    private static string Ai(IniText ini, double up, double down)
    {
        ini.Set("GEARS", "UP", up, "0");
        ini.Set("GEARS", "DOWN", down, "0");
        return ini.ToString();
    }

    /// <summary>Ratio choices of the car's setup screen would override the transmission's gears; a setup without any is left alone</summary>
    private static IniText Setup(IniText ini)
    {
        foreach (var section in ini.Sections.Where(s => s.StartsWith("GEAR_", StringComparison.Ordinal) || s == "FINAL_GEAR_RATIO").Distinct().ToList())
            ini.RemoveSection(section);
        return ini;
    }

    /// <summary>
    /// The game's turbo: boost climbs with engine speed as (rpm / reference)^gamma up to the wastegate, with a
    /// lag behind the throttle. Fitted to how much more the dyno's boosted curve makes than the same engine
    /// without its charger, so the gauge and the lag are the turbo's while the torque with the throttle open
    /// stays the dyno's (the curve is divided by the boost the game adds back).
    /// </summary>
    private static AcTurbo? Turbo(EngineReport report, DynoResult dyno)
    {
        if (!report.Turbocharged || report.Inputs is not { BoostMax: > 0 } inputs) return null;

        var natural = EngineDyno.Run(inputs with { BoostMax = 0 });
        var gain = dyno.Curve
            .Select(p => (p.Rpm, Gain: natural.TorqueAt(p.Rpm) > 0 ? Math.Max(0, p.Torque / natural.TorqueAt(p.Rpm) - 1) : 0))
            .ToList();
        var max = gain.Max(g => g.Gain);
        if (max < 0.02) return null;

        var reference = gain.First(g => g.Gain >= max * 0.95).Rpm;

        // Gamma by least squares on the way up: log(gain / max) against log(rpm / reference)
        double sumXy = 0, sumXx = 0;
        foreach (var (rpm, g) in gain.Where(g => g.Rpm < reference && g.Gain > max * 0.05))
        {
            var x = Math.Log(rpm / reference);
            var y = Math.Log(g / max);
            sumXy += x * y;
            sumXx += x * x;
        }

        var gamma = sumXx > 0 ? Math.Clamp(sumXy / sumXx, 0.5, 4) : 2;
        return new AcTurbo(max, reference, gamma);
    }

    private sealed record AcTurbo(double MaxBoost, double ReferenceRpm, double Gamma)
    {
        /// <summary>Boost the game settles at with the throttle open at this speed</summary>
        public double BoostAt(double rpm) => rpm <= 0 ? 0 : Math.Min(MaxBoost, MaxBoost * Math.Pow(rpm / ReferenceRpm, Gamma));
    }

    private static string Number(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
}
