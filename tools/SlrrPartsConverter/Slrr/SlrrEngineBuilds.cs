using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Street_Rod_AC.Parts;

namespace Street_Rod_AC.Slrr;

/// <summary>
/// Collects complete engines as part lists: the stock_parts_list_E of every car chassis script
/// (with its stage 1 and 2 kits), and the same kind of list from text files of build notes.
/// </summary>
public sealed class SlrrEngineBuilds
{
    private const string CarsFolder = @"cars\racers";
    private const string ChassisClass = "java.game.parts.bodypart.Chassis";

    private static readonly (string Field, string Stage)[] Stages =
    {
        ("stock_parts_list_E", "stock"), ("stg_1_parts_list_E", "stage1"), ("stg_2_parts_list_E", "stage2")
    };

    // stock_parts_list_E[ 1] = parts.engines.MOPAR:0x00000001r; // "MOPAR_block_340" //
    private static readonly Regex NoteEntry = new(
        @"parts_list_E\s*\[\s*(?<index>\d+)\s*\]\s*=\s*(?<rpk>[\w.]+):0x(?<id>[0-9A-Fa-f]+)r\s*;(?:\s*//\s*""?(?<name>[^""/]*))?",
        RegexOptions.Compiled);

    private static readonly Regex NoteListStart = new(@"parts_list_E\s*=\s*new\s+int", RegexOptions.Compiled);
    private static readonly Regex Power = new(@"(?<hp>\d+(?:[.,]\d+)?)\s*hp\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NotSlug = new("[^a-z0-9]+", RegexOptions.Compiled);
    private static readonly Regex CodeLine = new(@"^(else|if|for|while|do|return|int|float)\b", RegexOptions.Compiled);

    // Anything shorter is a fragment of a script, not an engine; mostly unresolved = a pack that is not installed
    private const int MinBuildParts = 10;
    private const double MinResolved = 0.8;

    private readonly SlrrGame _game;
    private readonly Dictionary<(SlrrRpk, int), string> _partIds;

    public SlrrEngineBuilds(SlrrGame game, Dictionary<(SlrrRpk, int), string> partIds)
    {
        _game = game;
        _partIds = partIds;
    }

    public List<EngineBuild> FromCars(SlrrScriptEvaluator scripts)
    {
        var builds = new List<EngineBuild>();
        var carsRoot = Path.Combine(_game.Root, CarsFolder);
        if (!Directory.Exists(carsRoot)) return builds;

        foreach (var file in Directory.EnumerateFiles(carsRoot, "*.rpk").OrderBy(f => f))
        {
            var rpk = _game.GetRpk(Path.GetRelativePath(_game.Root, file));
            if (rpk == null) continue;

            var car = Path.GetFileNameWithoutExtension(file);
            foreach (var entry in rpk.Entries.Values)
            {
                var scriptPath = entry.FirstValue("script");
                if (string.IsNullOrEmpty(scriptPath)) continue;

                var scriptFile = Path.Combine(_game.Root, scriptPath);
                if (!File.Exists(scriptFile) || !scripts.Extends(scriptFile, ChassisClass)) continue;

                var script = scripts.Evaluate(scriptFile);
                if (script == null) continue;

                foreach (var (field, stage) in Stages)
                {
                    if (!script.StockState.TryGetValue(field, out var list) && !script.Properties.TryGetValue(field, out list)) continue;
                    if (list is not List<object> items) continue;

                    var parts = items.OfType<string>().Select(FromResource).Where(p => p != null).Select(p => p!).ToList();
                    if (parts.Count < MinBuildParts) continue;

                    // A car rpk may hold several chassis (body variants), told apart by their type id
                    var id = $"cars/{car}/{entry.TypeId:X4}/{stage}";
                    var name = script.Properties.TryGetValue("vehicleName", out var vehicle) && vehicle is string { Length: > 0 } vehicleName && vehicleName != "unnamed"
                        ? vehicleName
                        : car;
                    builds.Add(new EngineBuild
                    {
                        Id = id,
                        Name = stage == "stock" ? name : $"{name} ({stage})",
                        Origin = EngineBuild.OriginCar,
                        Source = SlrrGame.Describe(rpk, entry.TypeId),
                        Parts = parts
                    });
                }
            }
        }

        return builds;
    }

    /// <summary>
    /// Build notes are loose text: a title line (often with the power, "MOPAR 340 Six Pack 290 hp ====")
    /// followed by the stock_parts_list_E lines of a car script.
    /// </summary>
    public List<EngineBuild> FromNotes(string folder)
    {
        var builds = new List<EngineBuild>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.txt").OrderBy(f => f))
        {
            var fileId = Slug(Path.GetFileNameWithoutExtension(file));
            string? title = null;
            EngineBuild? current = null;
            var indices = new HashSet<int>();

            foreach (var line in File.ReadLines(file, Encoding.Latin1))
            {
                var entry = NoteEntry.Match(line);
                if (entry.Success)
                {
                    if (current == null)
                    {
                        builds.Add(current = NewBuild(title ?? fileId));
                        indices.Clear();
                    }

                    // Notes copied from car scripts pick between engines at random: the first alternative wins
                    if (!indices.Add(int.Parse(entry.Groups["index"].Value))) continue;

                    var rpkPath = entry.Groups["rpk"].Value.Replace('.', '\\') + ".rpk";
                    var reference = Reference(rpkPath, System.Convert.ToInt32(entry.Groups["id"].Value, 16));
                    reference.Name = entry.Groups["name"].Value.Trim() is { Length: > 0 } name ? name : null;
                    current.Parts.Add(reference);
                    continue;
                }

                if (NoteListStart.IsMatch(line))
                {
                    if (current is { Parts.Count: >= MinBuildParts }) current = null;
                    continue;
                }

                // A line of prose is the title of whatever list comes next
                var text = line.Trim().Trim('=', '#', '/', '*', '-').Trim();
                if (text.Length > 2 && char.IsLetter(text[0]) && !CodeLine.IsMatch(text) && !text.Contains(';') && !text.Contains('(') && !text.Contains('{') && !text.Contains('='))
                {
                    title = text;
                    current = null;
                }
            }

            EngineBuild NewBuild(string name)
            {
                var id = $"notes/{fileId}/{Slug(name)}";
                for (var n = 2; builds.Any(b => b.Id == id); n++) id = $"notes/{fileId}/{Slug(name)}-{n}";

                var power = Power.Match(name);
                return new EngineBuild
                {
                    Id = id,
                    Name = name,
                    Origin = EngineBuild.OriginNotes,
                    Source = Path.GetFileName(file),
                    RatedPower = power.Success
                        ? double.Parse(power.Groups["hp"].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture)
                        : null
                };
            }
        }

        // The same notes tend to exist in several copies
        return builds
            .Where(b => b.Parts.Count >= MinBuildParts && b.Parts.Count(p => p.Part != null) >= MinResolved * b.Parts.Count)
            .GroupBy(b => b.Name + "|" + string.Join(",", b.Parts.Select(p => p.Source.ToLowerInvariant())))
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>"rpk path#0xID" as the script evaluator exports resources</summary>
    private PartStockReference? FromResource(string resource)
    {
        var separator = resource.LastIndexOf("#0x", StringComparison.Ordinal);
        if (separator < 0) return null;

        return Reference(resource[..separator], System.Convert.ToInt32(resource[(separator + 3)..], 16));
    }

    private PartStockReference Reference(string rpkPath, int typeId)
    {
        var rpk = _game.GetRpk(rpkPath);
        return new PartStockReference
        {
            Part = rpk != null && _partIds.TryGetValue((rpk, typeId), out var id) ? id : null,
            Source = $"{rpkPath}#0x{typeId:X4}"
        };
    }

    private static string Slug(string text) => NotSlug.Replace(text.ToLowerInvariant(), "-").Trim('-');
}
