using System.IO;
using System.Text.RegularExpressions;

namespace Street_Rod_AC.Parts.Export;

/// <summary>
/// A sound as Assetto Corsa understands it: an FMOD bank and the GUIDs that name its events. The game opens
/// <c>sfx\&lt;car&gt;.bank</c> by file name and finds <c>event:/cars/&lt;car&gt;/engine_ext</c> and the rest by GUID through
/// the car's <c>sfx\GUIDs.txt</c>, so a bank plays under any car id once its GUID lines are written for that id.
/// The bank's own GUIDs are the donor's and never change; only the text does.
/// </summary>
/// <param name="BankPath">The bank file, wherever it lives; it is never copied into memory</param>
/// <param name="GuidsText">The GUID lines as the donor has them, before any rewrite</param>
/// <param name="DonorId">The car id the GUID lines name (a library sound uses a placeholder)</param>
public sealed record CarSound(string BankPath, string GuidsText, string DonorId)
{
    /// <summary>The GUID lines for a car of this id: the donor's events under the car's name, nothing else</summary>
    public string GuidsFor(string carId) => AcCarSound.RewriteGuids(GuidsText, DonorId, carId);
}

public static class AcCarSound
{
    public const string SfxFolder = "sfx";
    public const string GuidsFileName = "GUIDs.txt";

    /// <summary>The bank Assetto Corsa loads for a car: named after the folder, nothing else is looked at</summary>
    public static string BankFileName(string carId) => carId + ".bank";

    /// <summary>The events a car needs for the engine to be heard from outside and inside, with the rev limiter</summary>
    public static readonly string[] EngineEvents = { "engine_ext", "engine_int", "limiter" };

    // {guid} kind:/path
    private static readonly Regex Line = new(@"^\s*\{([0-9a-fA-F-]{36})\}\s+([a-z]+):/(.*?)\s*$", RegexOptions.Compiled);

    /// <summary>
    /// A car's own sound. A car without its own <c>GUIDs.txt</c> (every Kunos car) names its events in the install's
    /// master file, <paramref name="masterGuidsPath"/>; null when the car has no bank or no GUIDs to go with it.
    /// </summary>
    public static CarSound? FromCar(string carDirectory, string? masterGuidsPath = null)
    {
        var id = Path.GetFileName(Path.TrimEndingDirectorySeparator(carDirectory));
        var bank = Path.Combine(carDirectory, SfxFolder, BankFileName(id));
        if (!File.Exists(bank)) return null;

        var guids = Path.Combine(carDirectory, SfxFolder, GuidsFileName);
        string text;
        if (File.Exists(guids)) text = File.ReadAllText(guids);
        else if (masterGuidsPath != null && File.Exists(masterGuidsPath)) text = File.ReadAllText(masterGuidsPath);
        else return null;

        return new CarSound(bank, text, id);
    }

    /// <summary>
    /// The donor's GUID lines written for <paramref name="carId"/>. Mod authors ship anything as GUIDs.txt, often the
    /// whole master file with a hundred other cars in it, so this keeps only what the bank can answer: buses, VCAs and
    /// snapshots as they are, the <c>common</c> bank and the donor's, the donor's own events and the ones outside any
    /// car (collisions, surfaces). The donor id is matched without regard to case, as the game does; a path that
    /// turns up twice keeps its first GUID.
    /// </summary>
    public static string RewriteGuids(string text, string donorId, string carId)
    {
        var lines = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var carsPrefix = "cars/" + donorId + "/";

        foreach (var raw in text.Split('\n'))
        {
            var m = Line.Match(raw.TrimEnd('\r'));
            if (!m.Success) continue;

            var guid = m.Groups[1].Value;
            var kind = m.Groups[2].Value;
            var path = m.Groups[3].Value;

            switch (kind)
            {
                case "bank":
                    if (path.Equals(donorId, StringComparison.OrdinalIgnoreCase)) path = carId;
                    else if (!path.Equals("common", StringComparison.OrdinalIgnoreCase)) continue;
                    break;

                case "event":
                    if (path.StartsWith(carsPrefix, StringComparison.OrdinalIgnoreCase)) path = "cars/" + carId + "/" + path[carsPrefix.Length..];
                    else if (path.StartsWith("cars/", StringComparison.OrdinalIgnoreCase)) continue;
                    break;
            }

            var key = kind + ":/" + path;
            if (seen.Add(key)) lines.Add("{" + guid + "} " + key);
        }

        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>The engine events of <see cref="EngineEvents"/> that GUID lines for <paramref name="carId"/> do not name</summary>
    public static IReadOnlyList<string> MissingEngineEvents(string guidsText, string carId)
    {
        var prefix = "event:/cars/" + carId + "/";
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in guidsText.Split('\n'))
        {
            var m = Line.Match(raw.TrimEnd('\r'));
            if (!m.Success) continue;
            var key = m.Groups[2].Value + ":/" + m.Groups[3].Value;
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) present.Add(key[prefix.Length..]);
        }

        return EngineEvents.Where(e => !present.Contains(e)).ToList();
    }
}
