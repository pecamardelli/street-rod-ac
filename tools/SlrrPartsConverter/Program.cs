using System.Diagnostics;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Street_Rod_AC.Parts;
using Street_Rod_AC.Parts.Scripting;
using Street_Rod_AC.Slrr;

namespace Street_Rod_AC;

/// <summary>
/// Converts the parts of a Street Legal Racing: Redline install into the game's own part format:
/// one folder per rpk with a pack.json (part definitions, slots, attach graph) and a KN5 model per part.
/// </summary>
public static class Program
{
    private const string PartsFolder = "parts";
    private const string BaseRpk = "parts.rpk";
    private const string BasePackId = "stock";
    private const string SlotRoleSuffix = "_slot_ID";
    private const string ConstantsFile = "script_constants.json";
    private const string NotesOption = "--notes";
    private const string ReplaceOption = "--replace";
    private const string DropOption = "--drop";
    private const string RenameOption = "--rename";
    private const string MergeOption = "--merge";
    private const string FitOption = "--fit";
    private const string ModelOption = "--model";
    private const string ShiftOption = "--shift";
    private const string SingleOption = "--single";
    private const string PadsOption = "--pads";
    private const string NameOption = "--name";
    private const string ShiftsOption = "--shifts";
    private const string AbsorbOption = "--absorb";
    private const string TakesPrefix = "takes:";

    /// <summary>Ids of the slots a pad of several carburettors is split into (the pad keeps its id for the first)</summary>
    private const int ExtraPadSlot = 300;

    /// <summary>Id of the slot over such a pad that takes the air cleaner spanning all its carburettors</summary>
    private const int SharedAirSlot = PartSlot.SharedAirSlot;

    private sealed record SourcePart(SlrrRpk Rpk, SlrrRpkEntry Entry, string ConfigFile, string? ScriptPath, string Id, string Name);

    /// <summary>
    /// A part that is left out, with the part that takes its place everywhere it was named or fitted; a set of
    /// several identical parts is replaced by that many (a dual-quad set by two carburettors)
    /// </summary>
    private sealed record MergeRule(Regex Pattern, string KeptId, int Count);

    /// <summary>A model of several identical items in a row (a carburettor set) kept as one item; builds get that many</summary>
    private sealed record SingleRule(Regex Pattern, int Count, float Spacing);

    /// <summary>
    /// A pad that took a set of carburettors becomes one pad per carburettor, plus a slot over them for the air
    /// cleaner that spans the set
    /// </summary>
    private sealed record PadRule(Regex Pattern, int Slot, string Fitting, int Count, float Spacing, float[] AirOffset, string? AirFitting);

    private sealed record NameRule(Regex Pattern, string Name);

    /// <summary>A standard fitting given to a slot of every part a pattern picks out</summary>
    private sealed record FitRule(Regex Pattern, int Slot, List<string> Fittings);

    /// <summary>Parts that are drawn with another part's model (the same product modelled better in another pack)</summary>
    private sealed record ModelRule(Regex Pattern, string DonorId);

    /// <summary>A slot moved in its part's space, to bring one pack's slot convention onto another's</summary>
    private sealed record ShiftRule(Regex Pattern, int Slot, float[] Offset);

    /// <summary>
    /// Where the parts of an rpk go: all of them (no selector), or those a selector picks out, by the name of a
    /// script class they descend from, a category they are filed under, or their own name (* for anything)
    /// </summary>
    private sealed record RenameRule(string Rpk, string? Selector, string PackId)
    {
        /// <summary>The selector as a pattern, null for the rule that takes the rest of the rpk</summary>
        public Regex? Pattern { get; } = Selector == null ? null : Program.Pattern(Selector);
    }

    public static int Main(string[] args)
    {
        // --notes <folder>: text files with engine builds written down as stock_parts_list_E lines
        // --replace <old pack>=<new pack>: the old pack stays out, what names its parts gets their twins of the new one
        // --drop <part id pattern>: parts left out altogether (a pack's take on engines another pack does better)
        // --rename <rpk pack>[:<selector>]=<pack id>: the pack, or the parts of it a selector picks out, go by a name
        //   of our own (the mod's file name says nothing to a player, and one rpk may hold rims and tyres both)
        // --merge <part id pattern>=<part id>: the parts are left out and the named part stands in for them: it is
        //   what builds, saves and attach lines naming them get, and it fits wherever they fitted
        // --fit <part id pattern>:<slot>=<fitting>[+<fitting>]: the slot mounts by a standard fitting, so it goes on
        //   every slot that takes it, whatever the pack; the slots that take it are found from the attach lines
        //   (a fitting written "takes:carb:4bbl" marks the slot as one that takes it instead)
        // --model <part id pattern>=<part id>: the parts are drawn with the named part's model (and its slot
        //   geometry), keeping their own scripts: the same product, modelled better in another pack
        // --shift <part id pattern>:<slot>=<dx>/<dy>/<dz>: the slot moves in its part's space (metres), to bring
        //   one pack's slot convention onto another's where parts of different packs meet by a fitting
        // --single <part id pattern>=<count>@<spacing>: the model draws <count> identical items in a row (a set of
        //   carburettors); one is kept, builds naming the part get <count> of it. A merge "=<part id>*<count>"
        //   does the same for a set that another part stands in for
        // --pads <part id pattern>:<slot>=<fitting>*<count>@<spacing>@<dx>/<dy>/<dz>[@<air fitting>]: a pad that took
        //   a set becomes <count> pads taking <fitting>, plus a slot over them (at the offset) for an air cleaner
        //   spanning the set
        // --name <part id pattern>=<display name>: what the part is called once it is not what its script says
        // --shifts <file>: slots nudged into place in the garage (slot_shifts.json), kept for good: what the game
        //   wrote next to the packs since the last run is folded into this file first, then all of it is applied
        // --absorb <slot_shifts.json>,...: more of the game's files to fold in (the game writes next to the content
        //   it runs on, in a build folder) and take away
        string? notes = null;
        string? shiftsFile = null;
        var absorb = new List<string>();
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var renames = new List<RenameRule>();
        var drops = new List<Regex>();
        var merges = new List<MergeRule>();
        var fits = new List<FitRule>();
        var modelRules = new List<ModelRule>();
        var shifts = new List<ShiftRule>();
        var singleRules = new List<SingleRule>();
        var padRules = new List<PadRule>();
        var nameRules = new List<NameRule>();
        var positional = new List<string>();
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == NotesOption && i + 1 < args.Length) notes = args[++i];
            else if (args[i] == ShiftsOption && i + 1 < args.Length) shiftsFile = args[++i];
            else if (args[i] == AbsorbOption && i + 1 < args.Length) absorb.AddRange(args[++i].Split(',').Select(f => f.Trim()).Where(f => f.Length > 0));
            else if (args[i] == DropOption && i + 1 < args.Length)
            {
                drops.AddRange(args[++i].Split(',').Select(p => Pattern(p.Trim())));
            }
            else if (args[i] == MergeOption && i + 1 < args.Length)
            {
                foreach (var pair in args[++i].Split(',').Select(p => p.Split('=')))
                {
                    var kept = pair.Length == 2 ? pair[1].Trim().Split('*') : Array.Empty<string>();
                    var count = 1;
                    if (kept.Length is < 1 or > 2 || (kept.Length == 2 && !int.TryParse(kept[1], out count)))
                    {
                        Console.WriteLine($"{MergeOption} takes <part id pattern>=<part id>[*<count>], e.g. engines/gm/stock_2x4brl_carburator=engines/gm/stock_4brl_carburator*2");
                        return 1;
                    }

                    merges.Add(new MergeRule(Pattern(pair[0].Trim()), kept[0], count));
                }
            }
            else if (args[i] == SingleOption && i + 1 < args.Length)
            {
                foreach (var rule in args[++i].Split(','))
                {
                    var pair = rule.Split('=', 2);
                    var value = pair.Length == 2 ? pair[1].Split('@') : Array.Empty<string>();
                    if (value.Length != 2 || !int.TryParse(value[0], out var count) || !float.TryParse(value[1], System.Globalization.NumberStyles.Float, culture, out var spacing))
                    {
                        Console.WriteLine($"{SingleOption} takes <part id pattern>=<count>@<spacing>, e.g. engines/generic/Carburetors_2x4BRL_King_Demon=2@0.22");
                        return 1;
                    }

                    singleRules.Add(new SingleRule(Pattern(pair[0].Trim()), count, spacing));
                }
            }
            else if (args[i] == PadsOption && i + 1 < args.Length)
            {
                foreach (var rule in args[++i].Split(','))
                {
                    var pair = rule.Split('=', 2);
                    var colon = pair[0].LastIndexOf(':');
                    var value = pair.Length == 2 ? pair[1].Split('@') : Array.Empty<string>();
                    var fitting = value.Length > 0 ? value[0].Split('*') : Array.Empty<string>();
                    var offset = value.Length > 2 ? value[2].Split('/') : Array.Empty<string>();
                    if (colon < 0 || !int.TryParse(pair[0][(colon + 1)..], out var slot) || value.Length is < 3 or > 4 || fitting.Length != 2
                        || !int.TryParse(fitting[1], out var count) || !float.TryParse(value[1], System.Globalization.NumberStyles.Float, culture, out var spacing)
                        || offset.Length != 3 || !offset.All(o => float.TryParse(o, System.Globalization.NumberStyles.Float, culture, out _)))
                    {
                        Console.WriteLine($"{PadsOption} takes <part id pattern>:<slot>=<fitting>*<count>@<spacing>@<dx>/<dy>/<dz>[@<air fitting>], " +
                                          "e.g. engines/chrysler/dualquad_intake:7=carb:4bbl*2@0.22@-0.043/0.108/-0.04@air:2x4");
                        return 1;
                    }

                    padRules.Add(new PadRule(Pattern(pair[0][..colon].Trim()), slot, fitting[0], count, spacing,
                        offset.Select(o => float.Parse(o, culture)).ToArray(), value.Length > 3 ? value[3] : null));
                }
            }
            else if (args[i] == NameOption && i + 1 < args.Length)
            {
                foreach (var pair in args[++i].Split(',').Select(p => p.Split('=', 2)))
                {
                    if (pair.Length != 2)
                    {
                        Console.WriteLine($"{NameOption} takes <part id pattern>=<display name>");
                        return 1;
                    }

                    nameRules.Add(new NameRule(Pattern(pair[0].Trim()), pair[1].Trim()));
                }
            }
            else if (args[i] == ModelOption && i + 1 < args.Length)
            {
                foreach (var pair in args[++i].Split(',').Select(p => p.Split('=')))
                {
                    if (pair.Length != 2)
                    {
                        Console.WriteLine($"{ModelOption} takes <part id pattern>=<part id>, e.g. engines/generic/Holley_4brl_carburator=engines/generic/Carburetors_4BRL_street_HOLLEY");
                        return 1;
                    }

                    modelRules.Add(new ModelRule(Pattern(pair[0].Trim()), pair[1].Trim()));
                }
            }
            else if (args[i] == ShiftOption && i + 1 < args.Length)
            {
                foreach (var rule in args[++i].Split(','))
                {
                    var pair = rule.Split('=', 2);
                    var colon = pair[0].LastIndexOf(':');
                    var offset = pair.Length == 2 ? pair[1].Split('/') : Array.Empty<string>();
                    if (colon < 0 || offset.Length != 3 || !int.TryParse(pair[0][(colon + 1)..], out var slot)
                        || !offset.All(o => float.TryParse(o, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)))
                    {
                        Console.WriteLine($"{ShiftOption} takes <part id pattern>:<slot>=<dx>/<dy>/<dz> in metres, e.g. engines/chrysler/Intake_manifold_*:7=0/-0.062/0");
                        return 1;
                    }

                    shifts.Add(new ShiftRule(Pattern(pair[0][..colon].Trim()), slot,
                        offset.Select(o => float.Parse(o, System.Globalization.CultureInfo.InvariantCulture)).ToArray()));
                }
            }
            else if (args[i] == FitOption && i + 1 < args.Length)
            {
                foreach (var rule in args[++i].Split(','))
                {
                    var pair = rule.Split('=', 2);
                    var colon = pair[0].LastIndexOf(':');
                    if (pair.Length != 2 || colon < 0 || !int.TryParse(pair[0][(colon + 1)..], out var slot))
                    {
                        Console.WriteLine($"{FitOption} takes <part id pattern>:<slot>=<fitting>[+<fitting>], e.g. engines/generic/Carburetors_4BRL_*:10=carb:4bbl");
                        return 1;
                    }

                    fits.Add(new FitRule(Pattern(pair[0][..colon].Trim()), slot, pair[1].Split('+').Select(f => f.Trim()).ToList()));
                }
            }
            else if (args[i] == ReplaceOption && i + 1 < args.Length)
            {
                foreach (var pair in args[++i].Split(',').Select(p => p.Split('=')))
                {
                    if (pair.Length != 2)
                    {
                        Console.WriteLine($"{ReplaceOption} takes <old pack>=<new pack>, e.g. engines/Mopar=engines/chrysler");
                        return 1;
                    }

                    replacements[pair[0].Trim()] = pair[1].Trim();
                }
            }
            else if (args[i] == RenameOption && i + 1 < args.Length)
            {
                foreach (var pair in args[++i].Split(',').Select(p => p.Split('=')))
                {
                    var source = pair.Length == 2 ? pair[0].Trim().Split(':') : Array.Empty<string>();
                    if (source.Length is < 1 or > 2)
                    {
                        Console.WriteLine($"{RenameOption} takes <rpk pack>[:<class, category or part name>]=<pack id>, " +
                                          "e.g. engines/Chrysler_V8_pak=engines/chrysler or wheels:Tyre=tyres/sl_tuners");
                        return 1;
                    }

                    var selector = source.Length == 2 ? source[1].Trim() : null;
                    renames.Add(new RenameRule(source[0].Trim(), selector, pair[1].Trim()));
                }
            }
            else if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                // An option nobody knows, or one whose value is missing (a shell drops an empty argument): taken as
                // the pack filter it would convert nothing and say so only in the totals
                Console.WriteLine($"Unknown option or missing value: {args[i]}");
                positional.Clear();
                break;
            }
            else positional.Add(args[i]);
        }

        if (positional.Count < 2)
        {
            Console.WriteLine("Usage: SlrrPartsConverter <SLRR folder> <output folder> [pack filter] [--notes <folder>] " +
                              "[--replace <old pack>=<new pack>] [--drop <part id pattern>,...] [--rename <rpk pack>[:<selector>]=<pack id>,...] " +
                              "[--merge <part id pattern>=<part id>,...] [--fit <part id pattern>:<slot>=<fitting>[+<fitting>],...] " +
                              "[--model <part id pattern>=<part id>,...] [--shift <part id pattern>:<slot>=<dx>/<dy>/<dz>,...] " +
                              "[--single <part id pattern>=<count>@<spacing>,...] [--pads <part id pattern>:<slot>=<fitting>*<count>@<spacing>@<dx>/<dy>/<dz>[@<air fitting>],...] " +
                              "[--name <part id pattern>=<display name>,...] [--shifts <slot_shifts.json kept for good>] [--absorb <slot_shifts.json written by the game>,...]");
            Console.WriteLine(@"  e.g. SlrrPartsConverter ""D:\Games\SLRR"" ""Street Rod AC\Assets\Parts"" engines/Mopar  (the full run: tools\convert-parts.ps1)");
            return 1;
        }

        var game = new SlrrGame(positional[0]);
        var scripts = new SlrrScriptEvaluator(game);
        var output = positional[1];
        var filter = positional.Count > 2 ? positional[2] : null;
        // Stand-ins, fittings and repointed air cleaners are found among the parts converted in the run: with one pack
        // converted, its pads take only what its own parts fit, and the other packs on disk are not brought in step
        if (filter != null && (merges.Count > 0 || fits.Count > 0 || padRules.Count > 0 || modelRules.Count > 0))
            Console.WriteLine($"Converting {filter} alone: what other packs fit on it (and it on them) is left out; run without a filter before the content ships");

        var partsRoot = Path.Combine(game.Root, PartsFolder);
        if (!Directory.Exists(partsRoot))
        {
            Console.WriteLine($"No '{PartsFolder}' folder in {game.Root}");
            return 1;
        }

        var stopwatch = Stopwatch.StartNew();

        // First pass: give every part an id, so slots can refer to parts of any pack. Routing reads class files
        // of parts that may be dropped: an evaluator of its own keeps their classes out of the game's scripts
        var routing = new SlrrScriptEvaluator(game);
        var packs = new List<(string Id, SlrrRpk Rpk, List<SourcePart> Parts)>();
        var partIds = new Dictionary<(SlrrRpk, int), string>();
        var dropped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Ids parts went by before, for saves made then: a pack renamed since, or replaced by a later release
        var aliases = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // The base game keeps its stock parts (running gear, accessories, neons) in an rpk next to the parts folder
        var baseRpk = Path.Combine(game.Root, BaseRpk);
        var files = Directory.EnumerateFiles(partsRoot, "*.rpk", SearchOption.AllDirectories).OrderBy(f => f).ToList();
        if (File.Exists(baseRpk)) files.Insert(0, baseRpk);

        var rpkIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(game.Root, file);
            var rpk = game.GetRpk(relativePath);
            if (rpk == null) continue;

            var rpkId = file == baseRpk
                ? BasePackId
                : Path.ChangeExtension(Path.GetRelativePath(partsRoot, file), null).Replace('\\', '/');
            rpkIds.Add(rpkId);

            // Packs are named after the rpk they come from unless renamed
            var rules = renames.Where(r => r.Rpk.Equals(rpkId, StringComparison.OrdinalIgnoreCase)).ToList();
            var selectors = rules.Where(r => r.Selector != null).ToList();
            var rest = rules.FirstOrDefault(r => r.Selector == null)?.PackId ?? rpkId;
            var parts = CollectParts(game, rpk, (entry, name, scriptPath) => PackOf(selectors, rest, rpk, entry, name, scriptPath));
            foreach (var part in parts.Where(part => drops.Any(d => d.IsMatch(part.Id))).ToList())
            {
                parts.Remove(part);
                dropped.Add(SlrrGame.Describe(rpk, part.Entry.TypeId));
            }

            foreach (var pack in parts.GroupBy(p => p.Id[..p.Id.LastIndexOf('/')]))
            {
                packs.Add((pack.Key, rpk, pack.ToList()));
                foreach (var part in pack)
                {
                    partIds[(part.Rpk, part.Entry.TypeId)] = part.Id;
                    // A part routed out of the rpk's own pack by a selector went by that pack's name in a save
                    // made before the selector was written
                    if (pack.Key != rpkId) aliases[$"{rpkId}/{part.Name}"] = part.Id;
                    if (pack.Key != rest) aliases[$"{rest}/{part.Name}"] = part.Id;
                }
            }
        }

        // Several rpks may be routed to one pack (the universal-fit parts of every engine pack); ids are the pack
        // plus the cfg name, unique within an rpk only
        var clashes = packs.SelectMany(p => p.Parts).GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList();
        if (clashes.Count > 0)
        {
            Console.WriteLine($"{RenameOption} routes parts of the same name into one pack: {string.Join(", ", clashes.Select(g => g.Key))}");
            return 1;
        }

        // A pack is named by what it holds: the parts a selector picks out go first, the rest where the rpk goes.
        // A selector matches a part's name, one of its categories, or a class it descends from by the class's
        // simple or full name
        string PackOf(List<RenameRule> selectors, string rest, SlrrRpk rpk, SlrrRpkEntry entry, string name, string? scriptPath)
        {
            List<string>? categories = null;
            List<string>? classes = null;
            foreach (var rule in selectors)
            {
                var pattern = rule.Pattern!;
                if (pattern.IsMatch(name)) return rule.PackId;

                categories ??= game.Categories(rpk, entry);
                if (categories.Any(pattern.IsMatch)) return rule.PackId;

                classes ??= scriptPath == null ? new List<string>() : routing.Classes(Path.Combine(game.Root, scriptPath)).ToList();
                if (classes.Any(c => pattern.IsMatch(c) || pattern.IsMatch(c[(c.LastIndexOf('.') + 1)..]))) return rule.PackId;
            }

            return rest;
        }

        // A rule naming an rpk that is not there is a typo, and its parts would quietly end up elsewhere
        var unmatched = renames.Where(r => !rpkIds.Contains(r.Rpk)).ToList();
        if (unmatched.Count > 0)
        {
            Console.WriteLine($"{RenameOption} names packs that are not there: {string.Join(", ", unmatched.Select(r => r.Rpk).Distinct())}");
            return 1;
        }

        Console.WriteLine($"Found {partIds.Count} parts in {packs.Count} packs" + (dropped.Count > 0 ? $", {dropped.Count} dropped" : ""));

        foreach (var (oldId, newId) in replacements)
        {
            // A pack and whatever else was routed out of the same rpk
            var oldPacks = PacksOf(oldId);
            var newPacks = PacksOf(newId);
            if (oldPacks.Count == 0 || newPacks.Count == 0)
            {
                Console.WriteLine($"Cannot replace {oldId} with {newId}: {(oldPacks.Count == 0 ? oldId : newId)} is not among the packs");
                return 1;
            }

            Console.WriteLine($"Replacing {oldId} with {newId}");
            var oldParts = oldPacks.SelectMany(p => p.Parts).ToList();
            var newParts = newPacks.SelectMany(p => p.Parts).ToList();
            foreach (var (from, to) in ReplacePack(game, oldParts, newParts, partIds)) aliases[from] = to;

            packs.RemoveAll(oldPacks.Contains);
        }

        List<(string Id, SlrrRpk Rpk, List<SourcePart> Parts)> PacksOf(string packId)
        {
            var named = packs.FirstOrDefault(p => p.Id.Equals(packId, StringComparison.OrdinalIgnoreCase));
            return named.Parts == null ? new() : packs.Where(p => p.Rpk == named.Rpk).ToList();
        }

        // Merged parts leave their packs; whatever pointed at one (a twin of a replaced pack included) points at
        // the part that stands in for it. Their configs are kept: the stand-in inherits where they fitted
        var merged = new List<(SourcePart Source, string KeptId)>();
        // How many of a part a build gets where it named one: a set of carburettors is that many single ones now
        var multiplicity = new Dictionary<(SlrrRpk, int), int>();
        // Sets of carburettors that became single ones, with the slot their air cleaner sat on and the pads they sat on
        var sets = new Dictionary<(SlrrRpk, int), (int Horn, List<(SlrrRpk Rpk, int TypeId, int Slot)> Pads)>();
        foreach (var (_, _, parts) in packs)
        {
            foreach (var part in parts.ToList())
            {
                var rule = merges.FirstOrDefault(m => m.Pattern.IsMatch(part.Id) && !m.KeptId.Equals(part.Id, StringComparison.OrdinalIgnoreCase));
                if (rule == null) continue;

                var keptId = rule.KeptId;
                parts.Remove(part);
                merged.Add((part, keptId));
                if (rule.Count > 1) RememberSet(part);
                aliases[part.Id] = keptId;
                // Whatever resolved to the part (a twin of a replaced pack included) resolves to the stand-in
                foreach (var key in partIds.Where(p => p.Value.Equals(part.Id, StringComparison.OrdinalIgnoreCase)).Select(p => p.Key).ToList())
                {
                    partIds[key] = keptId;
                    if (rule.Count > 1) multiplicity[key] = rule.Count;
                }
            }
        }

        var remaining = packs.SelectMany(p => p.Parts).Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownKept = merged.Select(m => m.KeptId).Where(id => !remaining.Contains(id)).Distinct().ToList();
        var unusedMerges = merges.Where(m => !merged.Any(p => m.Pattern.IsMatch(p.Source.Id))).ToList();
        if (unknownKept.Count > 0 || unusedMerges.Count > 0)
        {
            if (unknownKept.Count > 0) Console.WriteLine($"{MergeOption} names parts that are not there to stand in: {string.Join(", ", unknownKept)}");
            if (unusedMerges.Count > 0) Console.WriteLine($"{MergeOption} patterns that match no part: {string.Join(", ", unusedMerges.Select(m => m.Pattern))}");
            return 1;
        }

        if (merged.Count > 0) Console.WriteLine($"  {merged.Count} parts merged into {merged.Select(m => m.KeptId).Distinct().Count()}");

        // A set's air cleaner sat on the slot of the set that has no attach lines of its own (the mount slot has
        // them); it will sit over the pads the set's mount slot attached to
        void RememberSet(SourcePart part)
        {
            var config = SlrrPartConfig.Load(part.ConfigFile);
            var mount = config.Slots.FirstOrDefault(s => s.AttachesTo.Count > 0);
            var horn = config.Slots.FirstOrDefault(s => s.AttachesTo.Count == 0);
            if (mount == null || horn == null) return;

            var pads = new List<(SlrrRpk, int, int)>();
            foreach (var (partId, slotId) in mount.AttachesTo)
            {
                var (targetRpk, target) = game.Resolve(part.Rpk, partId);
                if (targetRpk != null && target != null) pads.Add((targetRpk, target.TypeId, slotId));
            }

            sets[(part.Rpk, part.Entry.TypeId)] = (horn.Id, pads);
        }

        // Parts drawn with another part's model: the donor is any remaining part
        var sources = packs.SelectMany(p => p.Parts).ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

        // Models of a row of identical items kept as one
        var singles = new Dictionary<string, SingleRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in singleRules)
        {
            var matching = sources.Values.Where(p => rule.Pattern.IsMatch(p.Id)).ToList();
            if (matching.Count == 0)
            {
                Console.WriteLine($"{SingleOption} {rule.Pattern} matches no part");
                return 1;
            }

            foreach (var part in matching)
            {
                singles[part.Id] = rule;
                RememberSet(part);
                foreach (var key in partIds.Where(p => p.Value.Equals(part.Id, StringComparison.OrdinalIgnoreCase)).Select(p => p.Key))
                {
                    multiplicity[key] = rule.Count;
                }
            }
        }
        var donors = new Dictionary<string, SourcePart>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in modelRules)
        {
            var borrowers = sources.Values.Where(p => rule.Pattern.IsMatch(p.Id) && !p.Id.Equals(rule.DonorId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (borrowers.Count == 0 || !sources.TryGetValue(rule.DonorId, out var donor))
            {
                Console.WriteLine(borrowers.Count == 0
                    ? $"{ModelOption} {rule.Pattern} matches no part"
                    : $"{ModelOption} names a model donor that is not there: {rule.DonorId}");
                return 1;
            }

            foreach (var borrower in borrowers) donors[borrower.Id] = donor;
        }

        if (donors.Count > 0) Console.WriteLine($"  {donors.Count} parts drawn with another part's model");

        // Second pass: models and definitions. The definitions are written once every pack is converted: a merged
        // part's fit is grafted onto its stand-in, and fittings are found across packs
        var converted = 0;
        var withoutModel = 0;
        var failures = new List<string>();
        var written = new Dictionary<string, (string Folder, PartPack Pack, Dictionary<string, string?> Models)>(StringComparer.OrdinalIgnoreCase);

        foreach (var (packId, rpk, parts) in packs)
        {
            if (filter != null && !packId.Equals(filter, StringComparison.OrdinalIgnoreCase)) continue;

            // A pack fed by several rpks is prepared once and takes the rest as they come
            if (!written.TryGetValue(packId, out var target))
            {
                var packFolder = Path.Combine(output, packId.Replace('/', Path.DirectorySeparatorChar));
                if (!PrepareFolder(packFolder))
                {
                    Console.WriteLine($"Skipped {packId}: {packFolder} exists and is not a converted pack");
                    continue;
                }

                written[packId] = target = (packFolder, new PartPack { Id = packId, Source = rpk.RelativePath }, new Dictionary<string, string?>());
            }
            else target.Pack.Source += ", " + rpk.RelativePath;

            var texturePrefix = packId.Replace('/', '_').ToLowerInvariant() + "__";
            foreach (var source in parts)
            {
                try
                {
                    var donor = donors.GetValueOrDefault(source.Id);
                    var single = singles.GetValueOrDefault(donor?.Id ?? source.Id);
                    var definition = Convert(game, scripts, source, donor, single, partIds, target.Folder, texturePrefix, target.Models);
                    target.Pack.Parts.Add(definition);

                    converted++;
                    if (definition.Model == null) withoutModel++;
                }
                catch (Exception ex)
                {
                    failures.Add($"{source.Id}: {ex.Message}");
                }
            }

            Console.WriteLine($"  {packId,-45} {parts.Count,4} parts from {rpk.RelativePath}");
        }

        var definitions = written.Values.SelectMany(w => w.Pack.Parts).ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
        // Merged parts' fit first, then geometry (pads split after the shift, so the new pads are where the pad
        // is), then what sat on sets moves over the pads, and fittings are derived from the lines as they end up
        Graft(game, merged, definitions, partIds);
        Shift(shifts, definitions);
        SplitPads(padRules, definitions);
        Repoint(game, sets, definitions, partIds, singles.Keys);
        Fit(fits, definitions);

        // Slots nudged into place in the garage, last: they were made against the geometry as it ends up here.
        // What the game wrote since the last run joins the kept file and leaves the output folder
        if (shiftsFile != null)
        {
            if (Path.GetFileName(shiftsFile) != SlotShifts.FileName || absorb.Any(f => Path.GetFileName(f) != SlotShifts.FileName))
            {
                Console.WriteLine($"{ShiftsOption} and {AbsorbOption} take files named {SlotShifts.FileName}");
                return 1;
            }

            var keptFolder = Path.GetDirectoryName(Path.GetFullPath(shiftsFile))!;
            var kept = SlotShifts.Load(keptFolder);
            var folded = 0;
            var absorbed = new List<string>();
            // The kept file itself is not one of the game's: folding it into itself would double it and take it away
            foreach (var folder in absorb.Select(f => Path.GetDirectoryName(Path.GetFullPath(f))!).Prepend(Path.TrimEndingDirectorySeparator(Path.GetFullPath(output)))
                         .Distinct(StringComparer.OrdinalIgnoreCase).Where(f => !f.Equals(keptFolder, StringComparison.OrdinalIgnoreCase)))
            {
                var fresh = SlotShifts.Load(folder);
                if (fresh.IsEmpty) continue;

                foreach (var (partId, slotId, offset) in fresh.All) kept.Add(partId, slotId, offset, save: false);
                folded += fresh.All.Count();
                absorbed.Add(fresh.Path!);
            }

            if (folded > 0)
            {
                // The game's files go only once what they held is safely in the kept one
                if (!kept.Save())
                {
                    Console.WriteLine($"Could not write {shiftsFile}: the game's slot shifts stay where they are");
                    return 1;
                }

                foreach (var file in absorbed) File.Delete(file);
                Console.WriteLine($"  {folded} slot shifts from the garage folded into {shiftsFile}");
            }

            var applied = kept.ApplyTo(definitions);
            if (!kept.IsEmpty) Console.WriteLine($"  {applied} slots moved by {shiftsFile}");
        }

        foreach (var rule in nameRules)
        {
            var named = definitions.Values.Where(d => rule.Pattern.IsMatch(d.Id)).ToList();
            if (named.Count == 0) Console.WriteLine($"    {NameOption} {rule.Pattern}: no part has it");
            foreach (var definition in named) definition.DisplayName = rule.Name;
        }
        foreach (var (folder, pack, models) in written.Values)
        {
            File.WriteAllText(Path.Combine(folder, PartPack.FileName), JsonConvert.SerializeObject(pack, Formatting.Indented));
            Console.WriteLine($"  {pack.Id,-45} {pack.Parts.Count,4} parts, {models.Values.Count(m => m != null),4} models");
        }

        // The game runs the part scripts itself. A filtered run adds the classes of its packs to what is there;
        // parts without their classes would be parts nobody finds on their slot.
        var copied = CopyScripts(game, scripts, Path.Combine(output, PartScripts.Folder), filter == null);
        Console.WriteLine($"  {copied} script classes");

        // Constants of the shared script classes (fuel types, price factors...), for the game's part logic.
        // A filtered run has not seen them all.
        if (filter == null)
        {
            // A run that converted nothing has not made the folder yet
            Directory.CreateDirectory(output);

            // Packs converted before under a name no longer produced (renamed, replaced, dropped whole) would be
            // loaded next to the current ones
            var current = packs.Select(p => Path.GetFullPath(Path.Combine(output, p.Id.Replace('/', Path.DirectorySeparatorChar))))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var stale in Directory.EnumerateFiles(output, PartPack.FileName, SearchOption.AllDirectories)
                         .Select(f => Path.GetFullPath(Path.GetDirectoryName(f)!)).Where(f => !current.Contains(f)).ToList())
            {
                RemovePack(stale);
                Console.WriteLine($"  removed {Path.GetRelativePath(output, stale)}: no longer converted");
            }

            File.WriteAllText(Path.Combine(output, ConstantsFile), JsonConvert.SerializeObject(scripts.Constants, Formatting.Indented));
            File.WriteAllText(Path.Combine(output, PartPack.AliasesFileName), JsonConvert.SerializeObject(aliases, Formatting.Indented));

            var engineBuilds = new SlrrEngineBuilds(game, partIds);
            // Cars get an evaluator of their own: their classes are of no use to the game
            var builds = engineBuilds.FromCars(new SlrrScriptEvaluator(game));
            if (notes != null && Directory.Exists(notes)) builds.AddRange(engineBuilds.FromNotes(notes));

            // An engine around a part that was dropped on purpose is no engine the game should offer
            var left = builds.RemoveAll(b => b.Parts.Any(p => p.Part == null && dropped.Contains(p.Source)));
            if (left > 0) Console.WriteLine($"  {left} engine builds left out, they use dropped parts");

            // A build that named a set of carburettors gets one carburettor per pad
            if (multiplicity.Count > 0)
            {
                var repeated = 0;
                foreach (var build in builds)
                {
                    for (var i = build.Parts.Count - 1; i >= 0; i--)
                    {
                        var part = build.Parts[i];
                        if (part.Part == null || !SlrrGame.TryParseReference(part.Source, out var rpkPath, out var typeId)) continue;
                        var rpk = game.GetRpk(rpkPath);
                        if (rpk == null || !multiplicity.TryGetValue((rpk, typeId), out var count)) continue;

                        for (var extra = 1; extra < count; extra++) build.Parts.Insert(i + 1, part);
                        repeated++;
                    }
                }

                if (repeated > 0) Console.WriteLine($"  {repeated} carburettor sets in builds became single carburettors, one per pad");
            }

            File.WriteAllText(Path.Combine(output, EngineBuild.FileName), JsonConvert.SerializeObject(builds, Formatting.Indented));

            Console.WriteLine($"  {builds.Count} engine builds ({builds.Count(b => b.RatedPower != null)} with a rated power, " +
                              $"{builds.Count(b => b.Parts.All(p => p.Part != null))} fully resolved)");
        }

        Console.WriteLine();
        Console.WriteLine($"Converted {converted} parts ({withoutModel} without a model) in {stopwatch.Elapsed.TotalSeconds:0.0} s");
        if (failures.Count > 0)
        {
            Console.WriteLine($"{failures.Count} failed:");
            foreach (var failure in failures) Console.WriteLine("  " + failure);
        }

        return 0;
    }

    private static Regex Pattern(string glob) =>
        new("^" + Regex.Escape(glob).Replace(@"\*", ".*") + "$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <param name="packOf">The pack a part goes to, from its rpk entry, cfg name and script path</param>
    private static List<SourcePart> CollectParts(SlrrGame game, SlrrRpk rpk, Func<SlrrRpkEntry, string, string?, string> packOf)
    {
        var parts = new List<SourcePart>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in rpk.Entries.Values)
        {
            var configPath = entry.Lines.FirstOrDefault(l => l.Length > 2 && l[0] == "native" && l[1] == "part")?[2];
            if (configPath == null) continue;

            var configFile = Path.Combine(game.Root, configPath);
            if (!File.Exists(configFile)) continue;

            // Several entries may share one cfg; the type id keeps their names apart
            var name = Path.GetFileNameWithoutExtension(configPath);
            if (!usedNames.Add(name))
            {
                name = $"{name}_{entry.TypeId:X4}";
                usedNames.Add(name);
            }

            var scriptPath = entry.FirstValue("script");
            parts.Add(new SourcePart(rpk, entry, configFile, scriptPath, $"{packOf(entry, name, scriptPath)}/{name}", name));
        }

        return parts;
    }

    /// <param name="donor">A part whose model this one is drawn with; its slots of the same ids give the geometry</param>
    /// <param name="single">When the model (the donor's when there is one) draws a row of identical items and one is kept</param>
    private static PartDefinition Convert(SlrrGame game, SlrrScriptEvaluator scripts, SourcePart source, SourcePart? donor, SingleRule? single,
        Dictionary<(SlrrRpk, int), string> partIds, string packFolder, string texturePrefix, Dictionary<string, string?> models)
    {
        var config = SlrrPartConfig.Load(source.ConfigFile);

        // Slot positions are in the model's space: the donor's model comes with the donor's geometry
        var geometry = config.Slots.ToDictionary(s => s, s => s);
        if (donor != null)
        {
            // A part that mounts by a single slot mounts by it whatever its number (GM carburettors hang by 10, the
            // Carter by 12)
            var donorConfig = SlrrPartConfig.Load(donor.ConfigFile);
            var mounting = config.Slots.Where(s => s.AttachesTo.Count > 0).ToList();
            var donorMounting = donorConfig.Slots.Where(s => s.AttachesTo.Count > 0).ToList();
            foreach (var slot in config.Slots)
            {
                var counterpart = donorConfig.Slots.FirstOrDefault(s => s.Id == slot.Id)
                                  ?? (mounting.Count == 1 && donorMounting.Count == 1 && slot == mounting[0] ? donorMounting[0] : null);
                if (counterpart == null) Console.WriteLine($"    {source.Id} slot {slot.Id}: {donor.Id} has none, it keeps its place on the old model");
                else geometry[slot] = counterpart;
            }
        }

        var scriptFile = source.ScriptPath == null ? null : Path.Combine(game.Root, source.ScriptPath);
        var script = scriptFile != null && File.Exists(scriptFile) ? scripts.Evaluate(scriptFile) : null;

        // Fields like crankshaft_slot_ID name the slot a kind of part goes on; 0 = the part has no such slot
        var properties = script?.Properties ?? new Dictionary<string, object>();
        var slotRoles = properties
            .Where(p => p.Key.EndsWith(SlotRoleSuffix, StringComparison.Ordinal) && p.Value is long)
            .ToDictionary(p => p.Key[..^SlotRoleSuffix.Length], p => (int)(long)p.Value);
        foreach (var role in slotRoles.Keys) properties.Remove(role + SlotRoleSuffix);

        return new PartDefinition
        {
            Id = source.Id,
            Name = source.Name,
            DisplayName = script?.DisplayName,
            BaseClass = script?.BaseClass,
            ClassChain = script?.ClassChain ?? new List<string>(),
            Properties = properties,
            Derived = script?.Derived ?? new Dictionary<string, object>(),
            SlotRoles = slotRoles.Where(r => r.Value > 0).ToDictionary(r => r.Key, r => r.Value),
            StockParts = (script?.StockParts ?? new List<SlrrStockPart>()).Select(StockReference).ToList(),
            RequiredSlots = (script?.RequiredSlots ?? new List<ScriptSlotRule>())
                .Select(r => new PartSlotRule { Slot = r.Slot, Message = r.Message }).ToList(),
            Categories = game.Categories(source.Rpk, source.Entry),
            Model = donor == null
                ? ConvertModel(game, source, config, packFolder, texturePrefix, models, single)
                : ConvertModel(game, donor, SlrrPartConfig.Load(donor.ConfigFile), packFolder, texturePrefix, models, single, source.Name),
            Mass = config.Mass,
            Config = config.Other,
            SourceTypeId = source.Entry.TypeId,
            SourceScript = source.ScriptPath,
            Slots = config.Slots.Select(slot => new PartSlot
            {
                Id = slot.Id,
                Name = slot.Name,
                Position = new[] { geometry[slot].Position.X, geometry[slot].Position.Y, geometry[slot].Position.Z },
                Rotation = new[] { geometry[slot].YawPitchRoll.X, geometry[slot].YawPitchRoll.Y, geometry[slot].YawPitchRoll.Z },
                DamageMode = slot.DamageMode,
                AttachesTo = slot.AttachesTo.Select(r => Reference(game, source.Rpk, r.PartId, r.SlotId, partIds)).ToList(),
                CompatibleWith = slot.CompatibleWith.Select(r => Reference(game, source.Rpk, r.PartId, r.SlotId, partIds)).ToList()
            }).ToList()
        };

        // Scripts name the rpk outright, so their ids need no external table
        PartStockReference StockReference(SlrrStockPart stock)
        {
            var rpk = game.GetRpk(stock.Rpk);
            return new PartStockReference
            {
                Part = rpk != null && partIds.TryGetValue((rpk, stock.TypeId), out var id) ? id : null,
                Name = stock.Name,
                Conditional = stock.Conditional,
                Source = $"{stock.Rpk}#0x{stock.TypeId:X4}"
            };
        }
    }

    /// <summary>A slot reference of a part's cfg resolved to a part id; the id of a merged part is its stand-in's</summary>
    private static PartSlotReference Reference(SlrrGame game, SlrrRpk sourceRpk, int partId, int slotId, Dictionary<(SlrrRpk, int), string> partIds)
    {
        var (targetRpk, target) = game.Resolve(sourceRpk, partId);
        return new PartSlotReference
        {
            Part = targetRpk != null && target != null && partIds.TryGetValue((targetRpk, target.TypeId), out var id) ? id : null,
            Slot = slotId,
            Source = SlrrGame.Describe(sourceRpk, partId)
        };
    }

    /// <summary>
    /// Gives every merged part's fit to the part standing in for it: what the merged part's slots attached to and
    /// stood in for, its stand-in's slots of the same ids do too. Slots are mostly numbered alike across packs (the
    /// engine framework fixes them: a carburettor hangs by 10, a transmission by 2); where a pack numbers a mounting
    /// slot its own way (air cleaners: 11 or 12), a part that mounts by a single slot mounts by it whatever its
    /// number. A slot the stand-in still lacks is a merge worth a look and is reported.
    /// </summary>
    private static void Graft(SlrrGame game, List<(SourcePart Source, string KeptId)> merged,
        Dictionary<string, PartDefinition> definitions, Dictionary<(SlrrRpk, int), string> partIds)
    {
        foreach (var (source, keptId) in merged)
        {
            // A filtered run converts one pack; the stand-in may be in another
            if (!definitions.TryGetValue(keptId, out var kept)) continue;

            var config = SlrrPartConfig.Load(source.ConfigFile);
            var mounting = config.Slots.Where(s => s.AttachesTo.Count > 0).ToList();
            var keptMounting = kept.Slots.Where(s => s.AttachesTo.Count > 0).ToList();
            foreach (var slot in config.Slots.Where(s => s.AttachesTo.Count > 0 || s.CompatibleWith.Count > 0))
            {
                var target = kept.Slots.FirstOrDefault(s => s.Id == slot.Id)
                             ?? (mounting.Count == 1 && keptMounting.Count == 1 && slot == mounting[0] ? keptMounting[0] : null);
                if (target == null)
                {
                    Console.WriteLine($"    {source.Id} slot {slot.Id} has no counterpart on {keptId}: what fitted there is lost");
                    continue;
                }

                Add(target.AttachesTo, slot.AttachesTo.Select(r => Reference(game, source.Rpk, r.PartId, r.SlotId, partIds)));
                Add(target.CompatibleWith, slot.CompatibleWith.Select(r => Reference(game, source.Rpk, r.PartId, r.SlotId, partIds)));
            }

            void Add(List<PartSlotReference> references, IEnumerable<PartSlotReference> grafted)
            {
                foreach (var reference in grafted)
                {
                    // The stand-in itself (the merged part named it, or was named by it as a sibling) is no fit
                    if (reference.Part != null && reference.Part.Equals(kept.Id, StringComparison.OrdinalIgnoreCase)) continue;
                    if (references.Any(r => r.Slot == reference.Slot && (r.Part ?? r.Source).Equals(reference.Part ?? reference.Source, StringComparison.OrdinalIgnoreCase))) continue;

                    references.Add(reference);
                }
            }
        }
    }

    /// <summary>
    /// Moves slots in their parts' space. Packs place the same joint by different conventions (the Chrysler pack's
    /// carburettor slot sits at the carburettor's mid-height and its pads 6 cm above the flange, GM's at the base and
    /// the flange), which cancels within a pack and shows where parts of two packs meet by a fitting: both sides of
    /// a pack's joint move by the same amount, so nothing moves within the pack.
    /// </summary>
    private static void Shift(List<ShiftRule> rules, Dictionary<string, PartDefinition> definitions)
    {
        foreach (var rule in rules)
        {
            var shifted = 0;
            foreach (var definition in definitions.Values.Where(d => rule.Pattern.IsMatch(d.Id)))
            {
                foreach (var slot in definition.Slots.Where(s => s.Id == rule.Slot))
                {
                    for (var axis = 0; axis < 3; axis++) slot.Position[axis] += rule.Offset[axis];
                    shifted++;
                }
            }

            if (shifted == 0) Console.WriteLine($"    {ShiftOption} {rule.Pattern} slot {rule.Slot}: no part has it");
        }
    }

    /// <summary>
    /// A pad that took a set of carburettors as one part becomes one pad per carburettor, in a row along the engine
    /// axis about where the pad was (the pad keeps its id for the middle one, or the front one of a pair, the lower
    /// Z: the pad the manifold script reads, and the item <see cref="SlrrKn5Slicer"/> keeps), plus a slot over the
    /// row for an air cleaner that spans the set, where the set's own air-horn slot was.
    /// </summary>
    private static void SplitPads(List<PadRule> rules, Dictionary<string, PartDefinition> definitions)
    {
        foreach (var rule in rules)
        {
            var split = 0;
            foreach (var definition in definitions.Values.Where(d => rule.Pattern.IsMatch(d.Id)))
            {
                var pad = definition.Slots.FirstOrDefault(s => s.Id == rule.Slot);
                if (pad == null)
                {
                    Console.WriteLine($"    {PadsOption} {rule.Pattern}: {definition.Id} has no slot {rule.Slot}");
                    continue;
                }

                var origin = (float[])pad.Position.Clone();
                var middle = (rule.Count - 1) / 2;
                var fittings = rule.Fitting.Split('+').ToList();
                pad.Takes.Clear();
                pad.Takes.AddRange(fittings);
                for (var k = 0; k < rule.Count; k++)
                {
                    var along = (k - (rule.Count - 1) / 2f) * rule.Spacing;
                    var slot = k == middle ? pad : new PartSlot
                    {
                        Id = ExtraPadSlot + k,
                        Name = pad.Name,
                        Rotation = (float[])pad.Rotation.Clone(),
                        DamageMode = pad.DamageMode,
                        Takes = new List<string>(fittings)
                    };
                    slot.Position = new[] { origin[0], origin[1], origin[2] + along };
                    if (slot != pad) definition.Slots.Add(slot);
                }

                var air = new PartSlot
                {
                    Id = SharedAirSlot,
                    Name = "air cleaner over the carburettors",
                    Position = new[] { origin[0] + rule.AirOffset[0], origin[1] + rule.AirOffset[1], origin[2] + rule.AirOffset[2] },
                    Rotation = (float[])pad.Rotation.Clone()
                };
                if (rule.AirFitting != null) air.Takes.Add(rule.AirFitting);
                definition.Slots.Add(air);
                split++;
            }

            if (split == 0) Console.WriteLine($"    {PadsOption} {rule.Pattern}: no part has it");
        }
    }

    /// <summary>
    /// Air cleaners that sat on a set of carburettors named the set's air-horn slot; the set is single carburettors
    /// now, so they go over the row instead: onto the shared air slot of every pad the set sat on.
    /// </summary>
    /// <param name="perItem">Parts that were a row of items themselves (a tri-power air box): each sits on its own carburettor still</param>
    private static void Repoint(SlrrGame game, Dictionary<(SlrrRpk, int), (int Horn, List<(SlrrRpk Rpk, int TypeId, int Slot)> Pads)> sets,
        Dictionary<string, PartDefinition> definitions, Dictionary<(SlrrRpk, int), string> partIds, IEnumerable<string> perItem)
    {
        if (sets.Count == 0) return;

        var skip = perItem.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var repointed = 0;
        foreach (var definition in definitions.Values.Where(d => !skip.Contains(d.Id)))
        {
            foreach (var slot in definition.Slots)
            {
                for (var i = slot.AttachesTo.Count - 1; i >= 0; i--)
                {
                    var reference = slot.AttachesTo[i];
                    if (!SlrrGame.TryParseReference(reference.Source, out var rpkPath, out var typeId)) continue;
                    var rpk = game.GetRpk(rpkPath);
                    if (rpk == null || !sets.TryGetValue((rpk, typeId), out var set) || reference.Slot != set.Horn) continue;

                    slot.AttachesTo.RemoveAt(i);
                    foreach (var pad in set.Pads)
                    {
                        if (!partIds.TryGetValue((pad.Rpk, pad.TypeId), out var padPartId) || !definitions.TryGetValue(padPartId, out var padPart)) continue;
                        if (padPart.Slots.All(s => s.Id != SharedAirSlot))
                        {
                            Console.WriteLine($"    {definition.Id} sat on a set of carburettors on {padPartId} slot {pad.Slot}, which has no shared air slot: it fits nowhere there now");
                            continue;
                        }

                        if (slot.AttachesTo.Any(r => r.Slot == SharedAirSlot && padPartId.Equals(r.Part, StringComparison.OrdinalIgnoreCase))) continue;
                        slot.AttachesTo.Add(new PartSlotReference { Part = padPartId, Slot = SharedAirSlot, Source = SlrrGame.Describe(pad.Rpk, pad.TypeId) });
                    }

                    repointed++;
                }
            }
        }

        Console.WriteLine($"    {repointed} air cleaner fits moved from carburettor sets onto the pads' shared air slots");
    }

    /// <summary>
    /// Gives slots their standard fittings, then finds the slots that take each: every slot a fitted slot attaches
    /// to by an attach line, written on either side. The game then mates by the fitting as well as by name, so a
    /// part fitted "carb:4bbl" goes on every pad that takes it, in any pack (a slot that stands in for such a pad
    /// takes it too: the game follows those at run time).
    /// </summary>
    private static void Fit(List<FitRule> rules, Dictionary<string, PartDefinition> definitions)
    {
        if (rules.Count == 0) return;

        foreach (var rule in rules)
        {
            var fitted = 0;
            foreach (var definition in definitions.Values.Where(d => rule.Pattern.IsMatch(d.Id)))
            {
                // A cfg may declare a slot id twice; every one of them is the slot
                var matching = definition.Slots.Where(s => s.Id == rule.Slot).ToList();
                if (matching.Count == 0)
                {
                    Console.WriteLine($"    {FitOption} {rule.Pattern}: {definition.Id} has no slot {rule.Slot}");
                    continue;
                }

                // "takes:fitting" marks a slot that takes the fitting: for pads no fitted part of their own pack names
                foreach (var slot in matching)
                {
                    foreach (var fitting in rule.Fittings)
                    {
                        var takes = fitting.StartsWith(TakesPrefix, StringComparison.OrdinalIgnoreCase);
                        var list = takes ? slot.Takes : slot.Fits;
                        var name = takes ? fitting[TakesPrefix.Length..] : fitting;
                        if (!list.Contains(name, StringComparer.OrdinalIgnoreCase)) list.Add(name);
                    }
                }

                fitted++;
            }

            if (fitted == 0) Console.WriteLine($"    {FitOption} {rule.Pattern}: no part has it");
        }

        var slots = definitions.Values
            .SelectMany(d => d.Slots.Select(s => (Part: d, Slot: s)))
            .ToLookup(e => (e.Part.Id.ToLowerInvariant(), e.Slot.Id), e => e.Slot);

        foreach (var slot in definitions.Values.SelectMany(d => d.Slots))
        {
            foreach (var reference in slot.AttachesTo.Where(r => r.Part != null))
            {
                foreach (var other in slots[(reference.Part!.ToLowerInvariant(), reference.Slot)])
                {
                    // The fitted slot names the pad, or the pad names the fitted slot. A slot that fits several
                    // ways (an oval cleaner for dual quads and tri-powers both) says nothing about which one the
                    // pad is: the two still mate by name, and the pad's own rules say what else it takes
                    Take(other, slot.Fits);
                    Take(slot, other.Fits);
                }
            }
        }

        var fittings = definitions.Values.SelectMany(d => d.Slots).SelectMany(s => s.Fits.Concat(s.Takes)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f);
        foreach (var fitting in fittings)
        {
            var fitters = definitions.Values.Where(d => d.Slots.Any(s => s.Fits.Contains(fitting, StringComparer.OrdinalIgnoreCase))).ToList();
            var takers = definitions.Values.Where(d => d.Slots.Any(s => s.Takes.Contains(fitting, StringComparer.OrdinalIgnoreCase))).ToList();
            Console.WriteLine($"    {fitting,-14} {fitters.Count,3} parts fit it, {takers.Count,3} take it: {Packs(fitters)} -> {Packs(takers)}");
        }

        static void Take(PartSlot slot, List<string> fittings)
        {
            if (fittings.Count != 1) return;
            foreach (var fitting in fittings.Where(f => !slot.Takes.Contains(f, StringComparer.OrdinalIgnoreCase))) slot.Takes.Add(fitting);
        }

        static string Packs(List<PartDefinition> parts) => string.Join(", ", parts
            .GroupBy(p => p.Id[..p.Id.LastIndexOf('/')], StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key} {g.Count()}"));
    }

    /// <summary>
    /// Writes the part's model and returns its file name: the most detailed LOD that resolves to a mesh,
    /// plus every render outside the LOD set. Renders that are not meshes (exhaust smoke, lights) drop out.
    /// Parts that draw the same meshes with the same textures share one model file.
    /// </summary>
    /// <param name="single">When the model draws a row of identical items and one is kept</param>
    /// <param name="name">Name of the model file, the part's own unless it is drawn with another part's model</param>
    private static string? ConvertModel(SlrrGame game, SourcePart source, SlrrPartConfig config, string packFolder,
        string texturePrefix, Dictionary<string, string?> models, SingleRule? single = null, string? name = null)
    {
        var selected = SelectRenders(game, source, config);
        if (selected.Count == 0) return null;

        var key = string.Join('|', selected.Select(r =>
            $"{r.MeshFile}:{string.Join(',', r.TextureFiles)}:{r.Render.Position}:{r.Render.YawPitchRoll}")).ToLowerInvariant();
        if (single != null) key += $"|one of {single.Count}";
        if (models.TryGetValue(key, out var existing)) return existing;

        name ??= source.Name;
        var pieces = selected.Select(r => new SlrrKn5Builder.Piece(SlrrMesh.Load(r.MeshFile), r.TextureFiles, r.Render.Matrix));
        var kn5 = SlrrKn5Builder.Build(name, pieces, texturePrefix, simplified => Console.WriteLine($"    {simplified}"));
        if (single != null)
        {
            var before = kn5.RootNode.Children.Sum(n => n.Indices.Length / 3);
            var keptAt = SlrrKn5Slicer.KeepOne(kn5, single.Count, single.Spacing);
            Console.WriteLine($"    {name}: one of {single.Count} kept ({keptAt:+0.000;-0.000} m along the row), {before} -> {kn5.RootNode.Children.Sum(n => n.Indices.Length / 3)} triangles");
        }

        if (kn5.RootNode.Children.Count == 0) return models[key] = null;

        var model = name + ".kn5";
        kn5.Save(Path.Combine(packFolder, model));
        return models[key] = model;
    }

    /// <summary>What a part draws: the most detailed LOD that resolves to a mesh, plus every render outside the LOD set</summary>
    private static List<(SlrrRender Render, string MeshFile, List<string?> TextureFiles)> SelectRenders(SlrrGame game, SourcePart source, SlrrPartConfig config)
    {
        var resolved = new List<(SlrrRender Render, string MeshFile, List<string?> TextureFiles)>();
        foreach (var render in config.Renders)
        {
            var (renderRpk, entry) = game.Resolve(source.Rpk, render.Id);
            if (renderRpk == null || entry == null) continue;

            var meshFile = game.SourceFile(renderRpk, entry.Ids("mesh").FirstOrDefault(-1));
            if (meshFile == null) continue;

            resolved.Add((render, meshFile, entry.Ids("texture").Select(id => game.SourceFile(renderRpk, id)).ToList()));
        }

        var bestLod = resolved.Max(r => r.Render.Lod);
        return resolved.Where(r => r.Render.Lod == null || r.Render.Lod == bestLod).ToList();
    }

    /// <summary>
    /// Points every part of a replaced pack at the part that takes its place, so that whatever names the old
    /// pack (car scripts, build notes, other packs) ends up with a part of the new one. Old parts without a
    /// twin stay unresolved. Returns the pairs by part id, for the game to read saves made with the old pack.
    /// </summary>
    private static SortedDictionary<string, string> ReplacePack(SlrrGame game, List<SourcePart> oldParts, List<SourcePart> newParts,
        Dictionary<(SlrrRpk, int), string> partIds)
    {
        // An evaluator of its own: the classes of a pack that is left out are of no use to the game
        var scripts = new SlrrScriptEvaluator(game);
        var geometry = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var twins = SlrrPackTwins.Match(oldParts.Select(Traits).ToList(), newParts.Select(Traits).ToList());

        var aliases = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (source, twin) in oldParts.Zip(twins))
        {
            if (twin.New == null) partIds.Remove((source.Rpk, source.Entry.TypeId));
            else partIds[(source.Rpk, source.Entry.TypeId)] = aliases[source.Id] = twin.New.Id;
        }

        Console.WriteLine($"  {twins.Count(t => t.New != null)} of {twins.Count} parts have a twin ({twins.Count(t => t.New != null && t.Doubt != null)} in doubt)");
        foreach (var twin in twins.Where(t => t.New == null || t.Doubt != null))
        {
            Console.WriteLine(twin.New == null
                ? $"    {twin.Old.Name,-40} no twin ({twin.Old.DisplayName})"
                : $"    {twin.Old.Name,-40} -> {twin.New.Name,-44} {twin.Doubt}");
        }

        return aliases;

        SlrrPartTraits Traits(SourcePart source)
        {
            var config = SlrrPartConfig.Load(source.ConfigFile);
            var scriptFile = source.ScriptPath == null ? null : Path.Combine(game.Root, source.ScriptPath);
            var script = scriptFile != null && File.Exists(scriptFile) ? scripts.Evaluate(scriptFile) : null;
            var renders = SelectRenders(game, source, config);

            var traits = new SlrrPartTraits
            {
                Id = source.Id,
                Name = source.Name,
                DisplayName = script?.DisplayName,
                BaseClass = script?.BaseClass,
                Slots = config.Slots.Select(s => s.Id).ToHashSet(),
                Geometry = renders.Select(r => GeometryOf(r.MeshFile)).ToHashSet(),
                Textures = renders.SelectMany(r => r.TextureFiles).Where(t => t != null)
                    .Select(t => Path.GetFileNameWithoutExtension(t!).ToLowerInvariant()).ToHashSet()
            };

            foreach (var (partId, _) in config.Slots.SelectMany(s => s.AttachesTo))
            {
                var (targetRpk, target) = game.Resolve(source.Rpk, partId);
                if (targetRpk != null && target != null && partIds.TryGetValue((targetRpk, target.TypeId), out var id)) traits.Neighbours.Add(id);
            }

            return traits;
        }

        string GeometryOf(string meshFile) =>
            geometry.TryGetValue(meshFile, out var hash) ? hash : geometry[meshFile] = SlrrPackTwins.GeometryHash(SlrrMesh.Load(meshFile));
    }

    /// <summary>
    /// The game runs the part scripts itself, so it gets the classes: everything the evaluation touched plus the
    /// shared part classes, in the folder layout class lookup depends on.
    /// </summary>
    /// <param name="replace">Whether the classes are all there are: what was in the folder before goes</param>
    private static int CopyScripts(SlrrGame game, SlrrScriptEvaluator scripts, string target, bool replace)
    {
        var files = new HashSet<string>(scripts.UsedClassFiles, StringComparer.OrdinalIgnoreCase);
        var shared = Path.Combine(game.Root, PartsFolder, "scripts");
        if (Directory.Exists(shared)) files.UnionWith(Directory.EnumerateFiles(shared, "*.class", SearchOption.AllDirectories));

        if (replace && Directory.Exists(target)) Directory.Delete(target, true);

        var root = Path.GetFullPath(game.Root);
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(root, Path.GetFullPath(file));
            if (relative.StartsWith("..", StringComparison.Ordinal)) continue;

            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
        }

        return files.Count;
    }

    /// <summary>Takes a converted pack out of the output; a folder that is anything else is left alone</summary>
    private static void RemovePack(string folder)
    {
        var packFile = Path.Combine(folder, PartPack.FileName);
        if (!File.Exists(packFile)) return;

        foreach (var file in Directory.EnumerateFiles(folder, "*.kn5")) File.Delete(file);
        File.Delete(packFile);
        if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
    }

    /// <summary>Empties a previously converted pack folder; refuses to touch anything else</summary>
    private static bool PrepareFolder(string folder)
    {
        if (Directory.Exists(folder))
        {
            var isPack = File.Exists(Path.Combine(folder, PartPack.FileName));
            var isEmpty = !Directory.EnumerateFileSystemEntries(folder).Any();
            if (!isPack && !isEmpty) return false;

            foreach (var file in Directory.EnumerateFiles(folder, "*.kn5")) File.Delete(file);
        }

        Directory.CreateDirectory(folder);
        return true;
    }
}
