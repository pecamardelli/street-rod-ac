using Street_Rod_AC.Models.Race;

namespace Street_Rod_AC.Parts.Export;

/// <summary>
/// The damage AC cannot be told about at the start of a race, put into the car's data instead. The race mode sets
/// the body and the engine's life itself; nothing sets a gearbox's damage or a bent corner, so:
/// <list type="bullet">
/// <item>a bent corner is the toe AC's own suspension damage gives: its steering rod is deflected by the bend, and
/// suspensions.ini has one TOE_OUT per axle, a steering rod length in metres like AC's MAX_DAMAGE, so an axle
/// gets the mean of its two corners;</item>
/// <item>a worn gearbox shifts slower and only engages closer to the right revs (drivetrain.ini's CHANGE_UP_TIME,
/// CHANGE_DN_TIME and VALID_SHIFT_RPM_WINDOW).</item>
/// </list>
/// </summary>
public static class AcDamageData
{
    private const string SuspensionsFile = "suspensions.ini";
    private const string DrivetrainFile = "drivetrain.ini";

    /// <summary>A gearbox about to give out takes this many times as long to shift</summary>
    private const double WornShiftTimeFactor = 3;

    /// <summary>...and engages within this share of the window a good one has</summary>
    private const double WornShiftWindowFactor = 0.4;

    /// <summary>File name to new content, for the files the damage changes; nothing for a car in good shape</summary>
    public static Dictionary<string, string> Generate(RaceStartState damage, Func<string, string?> readFile)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var bend = damage.SuspensionBend;
        if (bend.Length >= 4 && bend.Any(b => b > 0) && readFile(SuspensionsFile) is { } suspensions)
        {
            var ini = new IniText(suspensions);
            foreach (var (section, left, right) in new[] { ("FRONT", 0, 1), ("REAR", 2, 3) })
            {
                var toe = (Clamp01(bend[left]) + Clamp01(bend[right])) / 2 * RaceStartState.MaxSuspensionBendMetres;
                if (toe > 0 && ini.GetNumber(section, "TOE_OUT") is { } toeOut) ini.Set(section, "TOE_OUT", toeOut + toe, "0.000000");
            }

            if (ini.Changed) files[SuspensionsFile] = ini.ToString();
        }

        var wear = Clamp01(damage.GearboxWear);
        if (wear > 0 && readFile(DrivetrainFile) is { } drivetrain)
        {
            var ini = new IniText(drivetrain);
            var slower = 1 + (WornShiftTimeFactor - 1) * wear;
            ini.Scale("GEARBOX", "CHANGE_UP_TIME", slower, "0");
            ini.Scale("GEARBOX", "CHANGE_DN_TIME", slower, "0");
            ini.Scale("GEARBOX", "VALID_SHIFT_RPM_WINDOW", 1 - (1 - WornShiftWindowFactor) * wear, "0");
            if (ini.Changed) files[DrivetrainFile] = ini.ToString();
        }

        return files;
    }

    /// <remarks>
    /// Helpers.Unit.Clamp01 with ifNotFinite 0, kept here because EngineBench links this file without the helpers:
    /// a bend or a wear that is not a number counts as none, so the data is left as the car had it
    /// </remarks>
    private static double Clamp01(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
}
