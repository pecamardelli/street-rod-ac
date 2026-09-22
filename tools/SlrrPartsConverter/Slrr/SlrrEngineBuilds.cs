using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Street_Rod_AC.Parts;

namespace Street_Rod_AC.Slrr;

/// <summary>
/// Collects complete engines as part lists: the stock_parts_list_E of every car chassis script
/// (with its stage 1 and 2 kits), the same kind of list from text files of build notes, and the engine kits
/// the packs themselves ship.
/// </summary>
public sealed class SlrrEngineBuilds
{
    private const string CarsFolder = @"cars\racers";
    private const string ChassisClass = "java.game.parts.bodypart.Chassis";
    private const string SetClass = "java.game.parts.Set";

    private static readonly (string Field, string Stage)[] Stages =
    {
        ("stock_parts_list_E", "stock"), ("stg_1_parts_list_E", "stage1"), ("stg_2_parts_list_E", "stage2")
    };

    // stock_parts_list_E[ 1] = parts.engines.MOPAR:0x00000001r; // "MOPAR_block_340" //
    private static readonly Regex NoteEntry = new(
        @"parts_list_E\s*\[\s*(?<index>\d+)\s*\]\s*=\s*(?<rpk>[\w.]+):0x(?<id>[0-9A-Fa-f]+)r\s*;(?:\s*//\s*""?(?<name>[^""/]*))?",
        RegexOptions.Compiled);

    private static readonly Regex NoteListStart = new(@"parts_list_E\s*=\s*new\s+int", RegexOptions.Compiled);
    private static readonly Regex Power = new(@"(?<hp>\d+(?:[.,]\d+)*)\s*hp\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Thousands = new(@"^\d{1,3}(,\d{3})+$", RegexOptions.Compiled);
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
    /// The kits a pack ships: Set classes whose build() puts a list of the pack's parts in the inventory. Where a
    /// mod's author wrote complete engines that way (the Ford six and V8 packs, one per displacement), they are
    /// builds like any other; an upgrade kit (a blower with its manifold) is too short to be one.
    /// </summary>
    public List<EngineBuild> FromKits(SlrrScriptEvaluator scripts, IEnumerable<SlrrRpk> rpks)
    {
        var builds = new List<EngineBuild>();
        foreach (var rpk in rpks)
        {
            var pack = Path.GetFileNameWithoutExtension(rpk.RelativePath);
            foreach (var entry in rpk.Entries.Values)
            {
                var scriptPath = entry.FirstValue("script");
                if (string.IsNullOrEmpty(scriptPath)) continue;

                var scriptFile = Path.Combine(_game.Root, scriptPath);
                if (!File.Exists(scriptFile) || !scripts.Extends(scriptFile, SetClass)) continue;

                var kit = scripts.Kit(scriptFile);
                if (kit == null || kit.Parts.Count < MinBuildParts) continue;

                var className = Path.GetFileNameWithoutExtension(scriptPath);
                builds.Add(new EngineBuild
                {
                    Id = $"kits/{pack}/{Slug(className)}",
                    Name = kit.Name is { Length: > 0 } ? kit.Name : className,
                    Origin = EngineBuild.OriginKit,
                    Source = SlrrGame.Describe(rpk, entry.TypeId),
                    Parts = kit.Parts.Select(p => Reference(p.Rpk, p.TypeId)).ToList()
                });
            }
        }

        return builds;
    }

    /// <summary>
    /// Build notes are loose text: a title line (often with the power, "MOPAR 340 Six Pack 290 hp ====")
    /// followed by the stock_parts_list_E lines of a car script. A line of prose in the middle of a list
    /// ("// heads") is a comment, not a title: the list counts on from where it was, while after a title it starts over.
    /// A title names, and rates, only the first list under it; a stage kit that follows is not the 290 hp engine.
    /// </summary>
    public List<EngineBuild> FromNotes(string folder)
    {
        var builds = new List<EngineBuild>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.txt").OrderBy(f => f))
        {
            var fileId = Slug(Path.GetFileNameWithoutExtension(file));
            string? title = null;
            var titleUsed = false;
            string? prose = null;
            EngineBuild? current = null;
            var indices = new HashSet<int>();
            var lastIndex = -1;

            foreach (var line in File.ReadLines(file, Encoding.Latin1))
            {
                var entry = NoteEntry.Match(line);
                if (entry.Success)
                {
                    var index = int.Parse(entry.Groups["index"].Value);

                    // Prose before this entry was a title if a list starts here: there is none yet, or the numbering goes back
                    if (prose != null && (current == null || index <= lastIndex))
                    {
                        (title, titleUsed) = (prose, false);
                        current = null;
                    }

                    prose = null;
                    lastIndex = index;

                    if (current == null)
                    {
                        builds.Add(current = NewBuild(title ?? fileId, !titleUsed));
                        titleUsed = true;
                        indices.Clear();
                    }

                    // Notes copied from car scripts pick between engines at random: the first alternative wins
                    if (!indices.Add(index)) continue;

                    var rpkPath = entry.Groups["rpk"].Value.Replace('.', '\\') + ".rpk";
                    var reference = Reference(rpkPath, System.Convert.ToInt32(entry.Groups["id"].Value, 16));
                    reference.Name = entry.Groups["name"].Value.Trim() is { Length: > 0 } name ? name : null;
                    current.Parts.Add(reference);
                    continue;
                }

                if (NoteListStart.IsMatch(line))
                {
                    // Prose right before a new list is its title, whatever the numbers say
                    if (current is { Parts.Count: >= MinBuildParts } || prose != null) current = null;
                    continue;
                }

                // A line of prose: the title of the list that comes next, or a comment inside the one that is open
                var text = line.Trim().Trim('=', '#', '/', '*', '-').Trim();
                if (text.Length > 2 && char.IsLetter(text[0]) && !CodeLine.IsMatch(text) && !text.Contains(';') && !text.Contains('(') && !text.Contains('{') && !text.Contains('='))
                    prose = text;
            }

            EngineBuild NewBuild(string name, bool rated)
            {
                var id = $"notes/{fileId}/{Slug(name)}";
                for (var n = 2; builds.Any(b => b.Id == id); n++) id = $"notes/{fileId}/{Slug(name)}-{n}";

                var power = rated ? Power.Match(name) : Match.Empty;
                return new EngineBuild
                {
                    Id = id,
                    Name = name,
                    Origin = EngineBuild.OriginNotes,
                    Source = Path.GetFileName(file),
                    RatedPower = power.Success ? Horsepower(power.Groups["hp"].Value) : null
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

    /// <summary>"1,050" is a thousand and fifty, "10,5" and "10.5" are ten and a half</summary>
    private static double? Horsepower(string text)
    {
        text = Thousands.IsMatch(text) ? text.Replace(",", string.Empty) : text.Replace(',', '.');
        return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hp) ? hp : null;
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
