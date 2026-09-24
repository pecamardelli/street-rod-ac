using System.Diagnostics;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Street_Rod_AC.Helpers;
using Street_Rod_AC.Parts;
using Street_Rod_AC.Parts.Scripting;
using Street_Rod_AC.Slrr;
using static Street_Rod_AC.ConverterOptions;

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
    private const string TakesPrefix = "takes:";

    /// <summary>Exit code of a run that wrote its output but left parts, packs or rpks out: a script must not take it for a clean run</summary>
    private const int ExitIncomplete = 2;

    /// <summary>Ids of the slots a pad of several carburettors is split into (the pad keeps its id for the first)</summary>
    private const int ExtraPadSlot = 300;

    /// <summary>Id of the slot over such a pad that takes the air cleaner spanning all its carburettors</summary>
    private const int SharedAirSlot = PartSlot.SharedAirSlot;

    private sealed record SourcePart(SlrrRpk Rpk, SlrrRpkEntry Entry, string ConfigFile, string? ScriptPath, string Id, string Name);

    /// <summary>
    /// A pack converted before from an rpk that could not be read this time: kept as it was, not taken for a pack
    /// that left SLRR. Folder is where it goes in the output, From where it was read (the output itself, or the
    /// --previous content for a run into another folder), Root the content From belongs to
    /// </summary>
    private sealed record KeptPack(string Id, string Folder, string From, string Root, string Json, PartPack Pack);

    /// <summary>What a run works out, phase by phase; nothing of it reaches the output before <see cref="Write"/></summary>
    private sealed class Run
    {
        public Run(ConverterOptions options)
        {
            Options = options;
            Game = new SlrrGame(options.Slrr);
            Scripts = new SlrrScriptEvaluator(Game);
            Staging = ConversionOutput.StagingFolder(options.Output);
        }

        public ConverterOptions Options { get; }
        public SlrrGame Game { get; }

        /// <summary>The parts' own evaluator: the classes it touches are the ones the game gets</summary>
        public SlrrScriptEvaluator Scripts { get; }

        public string Output => Options.Output;
        public string? Filter => Options.Filter;
        public string PartsRoot => Path.Combine(Game.Root, PartsFolder);

        /// <summary>Where models and classes are written before they are moved into the output</summary>
        public string Staging { get; }

        public EarlierConversion? Earlier { get; set; }

        public List<(string Id, SlrrRpk Rpk, List<SourcePart> Parts)> Packs { get; } = new();
        public Dictionary<(SlrrRpk, int), string> PartIds { get; } = new();

        /// <summary>The keys of <see cref="PartIds"/> by the id they resolve to, kept in step as merges repoint them</summary>
        public Dictionary<string, List<(SlrrRpk, int)>> PartKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Dropped { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Ids parts went by before, for saves made then: a pack renamed since, or replaced by a later release</summary>
        public SortedDictionary<string, string> Aliases { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Merged parts leave their packs; whatever pointed at one (a twin of a replaced pack included) points at
        /// the part that stands in for it. Their configs are kept: the stand-in inherits where they fitted
        /// </summary>
        public List<(SourcePart Source, string KeptId)> Merged { get; } = new();

        /// <summary>How many of a part a build gets where it named one: a set of carburettors is that many single ones now</summary>
        public Dictionary<(SlrrRpk, int), int> Multiplicity { get; } = new();

        /// <summary>Sets of carburettors that became single ones, with the slot their air cleaner sat on and the pads they sat on</summary>
        public Dictionary<(SlrrRpk, int), (int Horn, List<(SlrrRpk Rpk, int TypeId, int Slot)> Pads)> Sets { get; } = new();

        /// <summary>Models of a row of identical items kept as one</summary>
        public Dictionary<string, SingleRule> Singles { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, SourcePart> Donors { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int Converted { get; set; }
        public int WithoutModel { get; set; }
        public List<string> Failures { get; } = new();
        public List<string> Skipped { get; } = new();

        /// <summary>Packs converted in the run: their folder in the output, their folder in the staging, what they hold</summary>
        public Dictionary<string, (string Folder, string Staging, PartPack Pack, Dictionary<string, string?> Models)> Written { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, PartDefinition> Definitions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Packs of unreadable rpks kept as they were, by pack id</summary>
        public Dictionary<string, KeptPack> Kept { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Whether an earlier run stopped while it moved its output in (its marker was there when this one started)</summary>
        public bool InterruptedBefore { get; set; }
    }

    /// <remarks>The options are described at <see cref="ConverterOptions.Parse"/></remarks>
    public static int Main(string[] args)
    {
        var options = ConverterOptions.Parse(args);
        if (options == null) return 1;

        var run = new Run(options);
        // Stand-ins, fittings and repointed air cleaners are found among the parts converted in the run: with one pack
        // converted, its pads take only what its own parts fit, and the other packs on disk are not brought in step
        if (options.Filter != null && (options.Merges.Count > 0 || options.Fits.Count > 0 || options.PadRules.Count > 0 || options.ModelRules.Count > 0))
            Console.WriteLine($"Converting {options.Filter} alone: what other packs fit on it (and it on them) is left out; run without a filter before the content ships");

        if (!Directory.Exists(run.PartsRoot))
        {
            Console.WriteLine($"No '{PartsFolder}' folder in {run.Game.Root}");
            return 1;
        }

        var stopwatch = Stopwatch.StartNew();

        // The earlier conversion is read before anything is written: it is usually the output folder itself
        run.Earlier = options.Previous == null ? null : EarlierConversion.Load(options.Previous);
        if (options.Previous != null && run.Earlier == null)
        {
            Console.WriteLine($"{PreviousOption} names no converted content: {options.Previous}");
            return 1;
        }

        if (!FirstPass(run)) return 1;

        if (options.Measure.Count > 0)
        {
            Measure(run.Game, run.Packs.SelectMany(p => p.Parts).Where(p => options.Measure.Any(m => m.IsMatch(p.Id))));
            // What an unreadable rpk holds was not measured: not a clean run
            if (run.Game.UnreadableRpks.Count == 0) return 0;

            Console.WriteLine($"{run.Game.UnreadableRpks.Count} rpks could not be read, their parts were not measured");
            return ExitIncomplete;
        }

        if (!ReplacePacks(run) || !MergeParts(run) || !ChooseModels(run)) return 1;

        // What a stopped run left: a classes folder parked mid-swap goes back before anything reads or replaces it,
        // and a commit that never finished is said out loud (only a full run makes that content whole again)
        try
        {
            var recovered = ConversionOutput.RecoverSwap(Path.Combine(options.Output, PartScripts.Folder));
            if (recovered != null) Console.WriteLine($"  {recovered}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Swapping the classes in over a parked copy that is still there would cost it
            Console.WriteLine($"Could not undo what a stopped run left of {PartScripts.Folder}: {ex.Message}");
            return 1;
        }
        run.InterruptedBefore = ConversionOutput.CommitWasInterrupted(options.Output);
        if (run.InterruptedBefore)
        {
            Console.WriteLine($"The last run stopped while it moved its output in ({ConversionOutput.CommitMarker(options.Output)}): " +
                              "the content is part that run's, part the one before" + (options.Filter == null ? "; this run replaces all of it" : "; run without a filter to make it whole"));
        }

        // Models and classes are made in a folder next to the output, and moved in only once the whole run has worked
        // out: a run that stops before the commit (a bad rule, an exception) leaves the content as the last run left
        // it. The commit itself is ordered so that a stop part way leaves no file naming what is not there (Write)
        ConversionOutput.ClearStaging(run.Staging, strict: true);
        try
        {
            ConvertPacks(run);
            Arrange(run);
            if (!FoldSlotShifts(run)) return 1;

            ApplyNames(run);
            if (!Write(run)) return 1;
        }
        finally
        {
            ConversionOutput.ClearStaging(run.Staging);
        }

        return Report(run, stopwatch);
    }

    /// <summary>
    /// First pass: gives every part an id, so slots can refer to parts of any pack. Routing reads class files of
    /// parts that may be dropped: an evaluator of its own keeps their classes out of the game's scripts
    /// </summary>
    private static bool FirstPass(Run run)
    {
        var game = run.Game;
        var options = run.Options;
        var routing = new SlrrScriptEvaluator(game);

        // The base game keeps its stock parts (running gear, accessories, neons) in an rpk next to the parts folder.
        // Sorted by ordinal: the order packs are met in shows in the output, which must not change with the culture
        var baseRpk = Path.Combine(game.Root, BaseRpk);
        var files = Directory.EnumerateFiles(run.PartsRoot, "*.rpk", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        if (File.Exists(baseRpk)) files.Insert(0, baseRpk);

        var rpkIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(game.Root, file);
            var rpkId = file == baseRpk
                ? BasePackId
                : Path.ChangeExtension(Path.GetRelativePath(run.PartsRoot, file), null).Replace('\\', '/');
            // An rpk that cannot be read is reported by the game and counted against the run. It is still there: a
            // rule naming it is no typo, and what was converted from it is kept (FindKeptPacks)
            rpkIds.Add(rpkId);
            var rpk = game.GetRpk(relativePath);
            if (rpk == null) continue;

            // Packs are named after the rpk they come from unless renamed
            var rules = options.Renames.Where(r => r.Rpk.Equals(rpkId, StringComparison.OrdinalIgnoreCase)).ToList();
            var selectors = rules.Where(r => r.Selector != null).ToList();
            var rest = rules.FirstOrDefault(r => r.Selector == null)?.PackId ?? rpkId;
            var parts = CollectParts(game, rpk, (entry, name, scriptPath) => PackOf(game, routing, selectors, rest, rpk, entry, name, scriptPath));
            foreach (var part in parts.Where(part => options.Drops.Any(d => d.IsMatch(part.Id))).ToList())
            {
                parts.Remove(part);
                run.Dropped.Add(SlrrGame.Describe(rpk, part.Entry.TypeId));
            }

            foreach (var pack in parts.GroupBy(p => p.Id[..p.Id.LastIndexOf('/')]))
            {
                run.Packs.Add((pack.Key, rpk, pack.ToList()));
                foreach (var part in pack)
                {
                    run.PartIds[(part.Rpk, part.Entry.TypeId)] = part.Id;
                    // A part routed out of the rpk's own pack by a selector went by that pack's name in a save
                    // made before the selector was written
                    if (pack.Key != rpkId) run.Aliases[$"{rpkId}/{part.Name}"] = part.Id;
                    if (pack.Key != rest) run.Aliases[$"{rest}/{part.Name}"] = part.Id;
                }
            }
        }

        // Several rpks may be routed to one pack (the universal-fit parts of every engine pack); ids are the pack
        // plus the cfg name, unique within an rpk only
        var clashes = run.Packs.SelectMany(p => p.Parts).GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList();
        if (clashes.Count > 0)
        {
            Console.WriteLine($"{RenameOption} routes parts of the same name into one pack: {string.Join(", ", clashes.Select(g => g.Key))}");
            return false;
        }

        // A rule naming an rpk that is not there is a typo, and its parts would quietly end up elsewhere
        var unmatched = options.Renames.Where(r => !rpkIds.Contains(r.Rpk)).ToList();
        if (unmatched.Count > 0)
        {
            Console.WriteLine($"{RenameOption} names packs that are not there: {string.Join(", ", unmatched.Select(r => r.Rpk).Distinct())}");
            return false;
        }

        Console.WriteLine($"Found {run.PartIds.Count} parts in {run.Packs.Count} packs" + (run.Dropped.Count > 0 ? $", {run.Dropped.Count} dropped" : ""));
        return true;
    }

    /// <summary>
    /// A pack is named by what it holds: the parts a selector picks out go first, the rest where the rpk goes.
    /// A selector matches a part's name, one of its categories, or a class it descends from by the class's
    /// simple or full name
    /// </summary>
    private static string PackOf(SlrrGame game, SlrrScriptEvaluator routing, List<RenameRule> selectors, string rest,
        SlrrRpk rpk, SlrrRpkEntry entry, string name, string? scriptPath)
    {
        List<string>? categories = null;
        List<string>? classes = null;
        foreach (var rule in selectors)
        {
            var pattern = rule.Pattern!;
            if (pattern.IsMatch(name)) return rule.PackId;

            categories ??= game.Categories(rpk, entry);
            if (categories.Any(pattern.IsMatch)) return rule.PackId;

            classes ??= game.ContentFile(scriptPath) is { } scriptFile ? routing.Classes(scriptFile).ToList() : new List<string>();
            if (classes.Any(c => pattern.IsMatch(c) || pattern.IsMatch(c[(c.LastIndexOf('.') + 1)..]))) return rule.PackId;
        }

        return rest;
    }

    /// <summary>Replaced packs leave, and whatever named their parts names the parts' twins in the packs that replace them</summary>
    private static bool ReplacePacks(Run run)
    {
        var twinRules = run.Options.TwinRules;
        var usedTwinRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (oldId, newId) in run.Options.Replacements)
        {
            // A pack and whatever else was routed out of the same rpk
            var oldPacks = PacksOf(run, oldId);
            var newPacks = PacksOf(run, newId);
            if (oldPacks.Count == 0 || newPacks.Count == 0)
            {
                Console.WriteLine($"Cannot replace {oldId} with {newId}: {(oldPacks.Count == 0 ? oldId : newId)} is not among the packs{UnreadableHint(run)}");
                return false;
            }

            Console.WriteLine($"Replacing {oldId} with {newId}");
            var oldParts = oldPacks.SelectMany(p => p.Parts).ToList();
            var newParts = newPacks.SelectMany(p => p.Parts).ToList();
            var pairs = ReplacePack(run.Game, oldParts, newParts, run.PartIds, twinRules, usedTwinRules);
            if (pairs == null) return false;
            foreach (var (from, to) in pairs) run.Aliases[from] = to;

            run.Packs.RemoveAll(oldPacks.Contains);
        }

        var unusedTwinRules = twinRules.Keys.Where(id => !usedTwinRules.Contains(id)).ToList();
        if (unusedTwinRules.Count > 0)
        {
            Console.WriteLine($"{TwinOption} names parts of no replaced pack: {string.Join(", ", unusedTwinRules)}{UnreadableHint(run)}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Added to a rule refused for naming what is not there, when rpks could not be read: what they hold is not among
    /// the parts. Refused all the same, before anything is written: a rule half applied would change other packs
    /// </summary>
    private static string UnreadableHint(Run run) => run.Game.UnreadableRpks.Count == 0
        ? string.Empty
        : $" (or is in one of the {run.Game.UnreadableRpks.Count} rpks that could not be read, above; nothing written)";

    private static List<(string Id, SlrrRpk Rpk, List<SourcePart> Parts)> PacksOf(Run run, string packId)
    {
        var named = run.Packs.FirstOrDefault(p => p.Id.Equals(packId, StringComparison.OrdinalIgnoreCase));
        return named.Parts == null ? new() : run.Packs.Where(p => p.Rpk == named.Rpk).ToList();
    }

    /// <summary>
    /// Merged parts leave their packs; whatever pointed at one (a twin of a replaced pack included) points at the
    /// part that stands in for it
    /// </summary>
    private static bool MergeParts(Run run)
    {
        var merges = run.Options.Merges;
        // Every key by the id it resolves to now (replaced packs have resolved to their twins), instead of a scan of
        // all the ids for every merged part
        foreach (var group in run.PartIds.GroupBy(p => p.Value, StringComparer.OrdinalIgnoreCase))
        {
            run.PartKeys[group.Key] = group.Select(p => p.Key).ToList();
        }

        foreach (var (_, _, parts) in run.Packs)
        {
            foreach (var part in parts.ToList())
            {
                var rule = merges.FirstOrDefault(m => m.Pattern.IsMatch(part.Id) && !m.KeptId.Equals(part.Id, StringComparison.OrdinalIgnoreCase));
                if (rule == null) continue;

                var keptId = rule.KeptId;
                parts.Remove(part);
                run.Merged.Add((part, keptId));
                if (rule.Count > 1) RememberSet(run, part);
                run.Aliases[part.Id] = keptId;
                // Whatever resolved to the part (a twin of a replaced pack included) resolves to the stand-in
                if (!run.PartKeys.Remove(part.Id, out var keys)) continue;

                foreach (var key in keys)
                {
                    run.PartIds[key] = keptId;
                    if (rule.Count > 1) run.Multiplicity[key] = rule.Count;
                }

                if (run.PartKeys.TryGetValue(keptId, out var keptKeys)) keptKeys.AddRange(keys);
                else run.PartKeys[keptId] = keys;
            }
        }

        var merged = run.Merged;
        var remaining = run.Packs.SelectMany(p => p.Parts).Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownKept = merged.Select(m => m.KeptId).Where(id => !remaining.Contains(id)).Distinct().ToList();
        var unusedMerges = merges.Where(m => !merged.Any(p => m.Pattern.IsMatch(p.Source.Id))).ToList();
        if (unknownKept.Count > 0 || unusedMerges.Count > 0)
        {
            if (unknownKept.Count > 0) Console.WriteLine($"{MergeOption} names parts that are not there to stand in: {string.Join(", ", unknownKept)}{UnreadableHint(run)}");
            if (unusedMerges.Count > 0) Console.WriteLine($"{MergeOption} patterns that match no part: {string.Join(", ", unusedMerges.Select(m => m.Pattern))}{UnreadableHint(run)}");
            return false;
        }

        if (merged.Count > 0) Console.WriteLine($"  {merged.Count} parts merged into {merged.Select(m => m.KeptId).Distinct().Count()}");
        return true;
    }

    /// <summary>
    /// A set's air cleaner sat on the slot of the set that has no attach lines of its own (the mount slot has
    /// them); it will sit over the pads the set's mount slot attached to
    /// </summary>
    private static void RememberSet(Run run, SourcePart part)
    {
        SlrrPartConfig config;
        try
        {
            config = SlrrPartConfig.Load(part.ConfigFile);
        }
        catch (Exception ex)
        {
            // The part fails again where it is converted, and is counted there
            Console.WriteLine($"    {part.Id}: {ex.Message}; what sat on the set stays where it was");
            return;
        }

        var mount = config.Slots.FirstOrDefault(s => s.AttachesTo.Count > 0);
        var horn = config.Slots.FirstOrDefault(s => s.AttachesTo.Count == 0);
        if (mount == null || horn == null) return;

        var pads = new List<(SlrrRpk, int, int)>();
        foreach (var (partId, slotId) in mount.AttachesTo)
        {
            var (targetRpk, target) = run.Game.Resolve(part.Rpk, partId);
            if (targetRpk != null && target != null) pads.Add((targetRpk, target.TypeId, slotId));
        }

        run.Sets[(part.Rpk, part.Entry.TypeId)] = (horn.Id, pads);
    }

    /// <summary>Parts whose model draws a row of items, and parts drawn with another part's model (the donor is any remaining part)</summary>
    private static bool ChooseModels(Run run)
    {
        var sources = run.Packs.SelectMany(p => p.Parts).ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var rule in run.Options.SingleRules)
        {
            var matching = sources.Values.Where(p => rule.Pattern.IsMatch(p.Id)).ToList();
            if (matching.Count == 0)
            {
                Console.WriteLine($"{SingleOption} {rule.Pattern} matches no part{UnreadableHint(run)}");
                return false;
            }

            foreach (var part in matching)
            {
                run.Singles[part.Id] = rule;
                RememberSet(run, part);
                foreach (var key in run.PartKeys.GetValueOrDefault(part.Id) ?? new List<(SlrrRpk, int)>())
                {
                    run.Multiplicity[key] = rule.Count;
                }
            }
        }

        foreach (var rule in run.Options.ModelRules)
        {
            var borrowers = sources.Values.Where(p => rule.Pattern.IsMatch(p.Id) && !p.Id.Equals(rule.DonorId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (borrowers.Count == 0 || !sources.TryGetValue(rule.DonorId, out var donor))
            {
                Console.WriteLine(borrowers.Count == 0
                    ? $"{ModelOption} {rule.Pattern} matches no part{UnreadableHint(run)}"
                    : $"{ModelOption} names a model donor that is not there: {rule.DonorId}{UnreadableHint(run)}");
                return false;
            }

            foreach (var borrower in borrowers) run.Donors[borrower.Id] = donor;
        }

        if (run.Donors.Count > 0) Console.WriteLine($"  {run.Donors.Count} parts drawn with another part's model");
        return true;
    }

    /// <summary>
    /// Second pass: models and definitions. The definitions are written once every pack is converted: a merged
    /// part's fit is grafted onto its stand-in, and fittings are found across packs. Models go to the staging folder
    /// </summary>
    private static void ConvertPacks(Run run)
    {
        foreach (var (packId, rpk, parts) in run.Packs)
        {
            if (run.Filter != null && !packId.Equals(run.Filter, StringComparison.OrdinalIgnoreCase)) continue;

            // A pack fed by several rpks is prepared once and takes the rest as they come
            if (!run.Written.TryGetValue(packId, out var target))
            {
                var packPath = packId.Replace('/', Path.DirectorySeparatorChar);
                var packFolder = Path.Combine(run.Output, packPath);
                if (!CanConvertInto(packFolder))
                {
                    Console.WriteLine($"Skipped {packId}: {packFolder} exists and is not a converted pack");
                    run.Skipped.Add($"{packId}: {packFolder} exists and is not a converted pack");
                    continue;
                }

                var staging = Path.Combine(run.Staging, packPath);
                Directory.CreateDirectory(staging);
                run.Written[packId] = target = (packFolder, staging, new PartPack { Id = packId, Source = rpk.RelativePath }, new Dictionary<string, string?>());
            }
            else target.Pack.Source += ", " + rpk.RelativePath;

            var texturePrefix = packId.Replace('/', '_').ToLowerInvariant() + "__";
            foreach (var source in parts)
            {
                try
                {
                    var donor = run.Donors.GetValueOrDefault(source.Id);
                    var single = run.Singles.GetValueOrDefault(donor?.Id ?? source.Id);
                    var definition = Convert(run.Game, run.Scripts, source, donor, single, run.PartIds, target.Staging, texturePrefix, target.Models);
                    target.Pack.Parts.Add(definition);

                    run.Converted++;
                    if (definition.Model == null) run.WithoutModel++;
                }
                catch (Exception ex)
                {
                    run.Failures.Add($"{source.Id}: {ex.Message}");
                }
            }

            Console.WriteLine($"  {packId,-45} {parts.Count,4} parts from {rpk.RelativePath}");
        }

        run.Definitions = run.Written.Values.SelectMany(w => w.Pack.Parts).ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Merged parts' fit first, then geometry (pads split after the shift, so the new pads are where the pad is), then
    /// what sat on sets moves over the pads, and fittings are derived from the lines as they end up
    /// </summary>
    private static void Arrange(Run run)
    {
        Graft(run.Game, run.Merged, run.Definitions, run.PartIds);
        Shift(run.Options.Shifts, run.Definitions);
        SplitPads(run.Options.PadRules, run.Definitions);
        Repoint(run.Game, run.Sets, run.Definitions, run.PartIds, run.Singles.Keys);
        Fit(run.Options.Fits, run.Definitions);
    }

    /// <summary>
    /// Slots nudged into place in the garage, last: they were made against the geometry as it ends up here.
    /// What the game wrote since the last run joins the kept file and leaves the output folder
    /// </summary>
    private static bool FoldSlotShifts(Run run)
    {
        var shiftsFile = run.Options.ShiftsFile;
        if (shiftsFile == null) return true;

        var absorb = run.Options.Absorb;
        if (Path.GetFileName(shiftsFile) != SlotShifts.FileName || absorb.Any(f => Path.GetFileName(f) != SlotShifts.FileName))
        {
            Console.WriteLine($"{ShiftsOption} and {AbsorbOption} take files named {SlotShifts.FileName}");
            return false;
        }

        var keptFolder = Path.GetDirectoryName(Path.GetFullPath(shiftsFile))!;
        var kept = SlotShifts.Load(keptFolder);
        if (kept.Problem != null)
        {
            // Folding into a file that could not be read would write over the user's shifts with only the new ones
            Console.WriteLine($"{shiftsFile}: {kept.Problem}. Fix or move it and run again");
            return false;
        }

        var folded = 0;
        var absorbed = new List<string>();
        // The kept file itself is not one of the game's: folding it into itself would double it and take it away
        foreach (var folder in absorb.Select(f => Path.GetDirectoryName(Path.GetFullPath(f))!).Prepend(Path.TrimEndingDirectorySeparator(Path.GetFullPath(run.Output)))
                     .Distinct(StringComparer.OrdinalIgnoreCase).Where(f => !f.Equals(keptFolder, StringComparison.OrdinalIgnoreCase)))
        {
            var fresh = SlotShifts.Load(folder);
            if (fresh.Problem != null)
            {
                // Not folded and not deleted: what the game wrote there is left for someone to look at
                Console.WriteLine($"  {fresh.Path}: {fresh.Problem}; skipped");
                continue;
            }
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
                return false;
            }

            foreach (var file in absorbed) File.Delete(file);
            Console.WriteLine($"  {folded} slot shifts from the garage folded into {shiftsFile}");
        }

        var applied = kept.ApplyTo(run.Definitions);
        if (!kept.IsEmpty) Console.WriteLine($"  {applied} slots moved by {shiftsFile}");
        return true;
    }

    private static void ApplyNames(Run run)
    {
        foreach (var rule in run.Options.NameRules)
        {
            var named = run.Definitions.Values.Where(d => rule.Pattern.IsMatch(d.Id)).ToList();
            if (named.Count == 0) Console.WriteLine($"    {NameOption} {rule.Pattern}: no part has it");
            foreach (var definition in named) definition.DisplayName = rule.Name;
        }
    }

    /// <summary>
    /// Works out every file of the output first (pack definitions, the classes the parts use and, for a full run,
    /// the constants, aliases and engine builds), and only then moves it all in (<see cref="Commit"/>). The packs of an
    /// rpk that could not be read are kept as the last conversion left them. False when the commit stopped part way
    /// </summary>
    private static bool Write(Run run)
    {
        var output = run.Output;
        FindKeptPacks(run);

        var packFiles = new List<(string Folder, string Staging, string Json)>();
        foreach (var (folder, staging, pack, models) in run.Written.Values)
        {
            // Made from the rpks that could be read; the pack as it was holds the unreadable one's parts too
            if (run.Kept.ContainsKey(pack.Id)) continue;

            packFiles.Add((folder, staging, JsonConvert.SerializeObject(pack, Formatting.Indented)));
            Console.WriteLine($"  {pack.Id,-45} {pack.Parts.Count,4} parts, {models.Values.Count(m => m != null),4} models");
        }

        foreach (var kept in run.Kept.Values)
        {
            Console.WriteLine($"  {kept.Id,-45} kept as it was: {kept.Pack.Source} could not be read");
            if (SamePath(kept.From, kept.Folder)) continue;

            // Kept from the --previous content for a run into another folder: its models go in through the staging
            // folder like the run's own (a folder of their own there: the run may have made some of the pack too)
            var staging = Path.Combine(run.Staging, ".kept", kept.Id.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(staging);
            foreach (var model in Directory.EnumerateFiles(kept.From, "*.kn5")) File.Copy(model, Path.Combine(staging, Path.GetFileName(model)));
            packFiles.Add((kept.Folder, staging, kept.Json));
        }

        // The game runs the part scripts itself. A filtered run adds the classes of its packs to what is there;
        // parts without their classes would be parts nobody finds on their slot. Taken before the engine kits
        // below are evaluated: the classes and constants those touch are no part's
        var classes = ScriptFiles(run.Game, run.Scripts);
        Console.WriteLine($"  {classes.Count} script classes");
        if (run.Filter == null) StageScripts(run, classes);

        string? constants = null;
        string? aliases = null;
        string? builds = null;
        var stale = new List<string>();
        // Constants of the shared script classes (fuel types, price factors...), for the game's part logic.
        // A filtered run has not seen them all.
        if (run.Filter == null)
        {
            // Packs converted before under a name no longer produced (renamed, replaced, dropped whole) would be
            // loaded next to the current ones. A pack whose rpk could not be read has not left SLRR: it is kept
            var current = run.Packs.Select(p => Path.GetFullPath(Path.Combine(output, p.Id.Replace('/', Path.DirectorySeparatorChar))))
                .Concat(run.Kept.Values.Select(k => Path.GetFullPath(k.Folder)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(output))
            {
                stale = Directory.EnumerateFiles(output, PartPack.FileName, SearchOption.AllDirectories)
                    .Select(f => Path.GetFullPath(Path.GetDirectoryName(f)!)).Where(f => !current.Contains(f)).ToList();
            }

            constants = Constants(run);

            // The parts the content ends up with: a kept pack's as it was, not what the run made of the rpks of it
            // that it could read
            var definitions = new Dictionary<string, PartDefinition>(run.Definitions, StringComparer.OrdinalIgnoreCase);
            foreach (var kept in run.Kept.Values)
            {
                if (run.Written.TryGetValue(kept.Id, out var written))
                {
                    foreach (var part in written.Pack.Parts) definitions.Remove(part.Id);
                }

                foreach (var part in kept.Pack.Parts) definitions[part.Id] = part;
            }

            if (run.Earlier != null)
            {
                var (renamed, kept) = run.Earlier.Carry(run.Game, run.PartIds, definitions, run.Aliases);
                if (renamed + kept > 0) Console.WriteLine($"  {renamed} parts renamed in their rpk and {kept} older aliases kept from {run.Options.Previous}");
            }

            KeepAliases(run, definitions);
            aliases = JsonConvert.SerializeObject(run.Aliases, Formatting.Indented);

            var engineBuilds = EngineBuilds(run);
            KeepBuilds(run, engineBuilds);
            builds = JsonConvert.SerializeObject(engineBuilds, Formatting.Indented);
        }

        return Commit(run, packFiles, classes, constants, aliases, builds, stale);
    }

    /// <summary>
    /// Moves the output in, in an order that leaves no file naming what is not there wherever it stops (a file held
    /// open, the process killed): what is named goes in before what names it, what nothing names any more goes last.
    /// <list type="number">
    /// <item>The classes, which the packs name (a full run swaps the folder in whole, <see cref="ConversionOutput.SwapIn"/>).</item>
    /// <item>Every pack's models: one moved over a model of the same name is the same part's, and no pack names a new one yet.</item>
    /// <item>Every pack.json, each followed by the removal of the models it no longer names.</item>
    /// <item>The constants, the engine builds and the aliases, which name the packs' parts.</item>
    /// <item>The packs no longer converted, once nothing written by the run leads to them.</item>
    /// </list>
    /// Each file goes in whole (renames, <see cref="SafeFile"/>). What a stop part way can leave is old files next to new
    /// ones: an old alias naming a part the new pack.json no longer has (a missing part, as for any save of a removed
    /// part). A marker next to the output is there from the first step to the last, so the next run says the content
    /// is a mix, and a full run, which rewrites every file, takes it away.
    /// </summary>
    private static bool Commit(Run run, List<(string Folder, string Staging, string Json)> packFiles, HashSet<string> classes,
        string? constants, string? aliases, string? builds, List<string> stale)
    {
        var output = run.Output;
        // A run that converted nothing has not made the folder yet
        Directory.CreateDirectory(output);
        ConversionOutput.BeginCommit(output);
        try
        {
            if (run.Filter == null)
            {
                // Nothing staged (no class at all) leaves the folder as it is
                ConversionOutput.SwapIn(Path.Combine(output, PartScripts.Folder), Path.Combine(run.Staging, PartScripts.Folder));
            }
            else CopyClasses(run.Game.Root, classes, Path.Combine(output, PartScripts.Folder));

            var made = packFiles.Select(p => MoveModelsIn(p.Folder, p.Staging)).ToList();
            for (var i = 0; i < packFiles.Count; i++) WritePack(packFiles[i].Folder, packFiles[i].Json, made[i]);

            if (run.Filter == null)
            {
                SafeFile.WriteAllText(Path.Combine(output, ConstantsFile), constants!);
                SafeFile.WriteAllText(Path.Combine(output, EngineBuild.FileName), builds!);
                SafeFile.WriteAllText(Path.Combine(output, PartPack.AliasesFileName), aliases!);

                foreach (var folder in stale)
                {
                    RemovePack(folder);
                    Console.WriteLine($"  removed {Path.GetRelativePath(output, folder)}: no longer converted");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Stopped while moving the output in: {ex.Message}");
            Console.WriteLine($"{output} is part this run's, part the last one's (every file whole); run again without a filter");
            return false;
        }

        // A filtered run does not make whole what a stopped full commit left: the marker stays for the full run that does
        if (run.Filter == null || !run.InterruptedBefore) ConversionOutput.EndCommit(output);
        return true;
    }

    /// <summary>
    /// The packs converted before from an rpk that could not be read this time, from the output and, for a full run,
    /// from the --previous content (a run into another folder). A pack gone from SLRR is stale; one that could not be
    /// read is not, and neither are its models, aliases and builds
    /// </summary>
    private static void FindKeptPacks(Run run)
    {
        if (run.Game.UnreadableRpks.Count == 0) return;

        // A filtered run commits only its own pack: the output's is the one it must not write over
        var roots = run.Filter == null ? ContentRoots(run) : ContentRoots(run).Where(r => SamePath(r, run.Output));
        foreach (var root in roots)
        {
            foreach (var file in Directory.EnumerateFiles(root, PartPack.FileName, SearchOption.AllDirectories))
            {
                var folder = Path.GetDirectoryName(file)!;
                var relative = Path.GetRelativePath(root, folder);
                var id = relative.Replace('\\', '/');
                if (run.Kept.ContainsKey(id) || (run.Filter != null && !run.Written.ContainsKey(id))) continue;

                string json;
                PartPack? pack;
                try
                {
                    json = File.ReadAllText(file);
                    pack = JsonConvert.DeserializeObject<PartPack>(json);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    Console.WriteLine($"  {file}: {ex.Message}; not kept");
                    continue;
                }

                if (pack == null || !pack.Source.Split(',').Select(s => s.Trim()).Any(s => s.Length > 0 && run.Game.IsUnreadable(s))) continue;

                run.Kept[id] = new KeptPack(id, Path.Combine(run.Output, relative), folder, root, json, pack);
            }
        }
    }

    /// <summary>The converted content there is to keep things from: the output as it is, then the --previous content</summary>
    private static List<string> ContentRoots(Run run)
    {
        var roots = new List<string>();
        if (Directory.Exists(run.Output)) roots.Add(run.Output);
        var previous = run.Options.Previous;
        if (previous != null && Directory.Exists(previous) && !SamePath(previous, run.Output)) roots.Add(previous);
        return roots;
    }

    /// <summary>The content the kept packs came from, the output first</summary>
    private static List<string> KeptRoots(Run run) =>
        run.Kept.Values.Select(k => k.Root).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    /// <summary>A JSON file of the content, or null when it is not there or cannot be read (said, not fatal: it only feeds what is kept)</summary>
    private static T? ReadJson<T>(string file) where T : class
    {
        if (!File.Exists(file)) return null;

        try
        {
            return JsonConvert.DeserializeObject<T>(File.ReadAllText(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.WriteLine($"  {file}: {ex.Message}; nothing kept from it");
            return null;
        }
    }

    /// <summary>The constants of the classes the run evaluated, plus those of the kept packs' classes it could not evaluate</summary>
    private static string Constants(Run run)
    {
        if (run.Kept.Count == 0) return JsonConvert.SerializeObject(run.Scripts.Constants, Formatting.Indented);

        var merged = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var (className, values) in run.Scripts.Constants) merged[className] = values;
        foreach (var root in KeptRoots(run))
        {
            var old = ReadJson<Dictionary<string, JToken>>(Path.Combine(root, ConstantsFile));
            if (old == null) continue;

            foreach (var (className, values) in old) merged.TryAdd(className, values);
        }

        return JsonConvert.SerializeObject(merged, Formatting.Indented);
    }

    /// <summary>
    /// The aliases of the content the kept packs came from that lead to their parts: saves name those parts by
    /// them, and with the rpk unreadable nothing else in the run knows they are still there
    /// </summary>
    private static void KeepAliases(Run run, Dictionary<string, PartDefinition> definitions)
    {
        // As many hops as the game follows (PartsCatalog.CurrentId)
        const int maxHops = 8;
        if (run.Kept.Count == 0) return;

        var keptIds = run.Kept.Values.SelectMany(k => k.Pack.Parts).Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var root in KeptRoots(run))
        {
            var old = ReadJson<Dictionary<string, string>>(Path.Combine(root, PartPack.AliasesFileName));
            if (old == null) continue;

            var oldAliases = new Dictionary<string, string>(old, StringComparer.OrdinalIgnoreCase);
            foreach (var (gone, target) in old)
            {
                if (definitions.ContainsKey(gone) || run.Aliases.ContainsKey(gone) || !Leads(target)) continue;

                run.Aliases[gone] = target;
                added++;
            }

            bool Leads(string id)
            {
                for (var hops = 0; hops < maxHops; hops++)
                {
                    if (keptIds.Contains(id)) return true;
                    if (!run.Aliases.TryGetValue(id, out var next) && !oldAliases.TryGetValue(id, out next)) return false;

                    id = next;
                }

                return false;
            }
        }

        if (added > 0) Console.WriteLine($"  {added} aliases of the kept packs kept");
    }

    /// <summary>
    /// The engine builds of the converted content that name an rpk that could not be read (the car's, a part's)
    /// stay as they were: made now, they would miss the car or have those parts unresolved
    /// </summary>
    private static void KeepBuilds(Run run, List<EngineBuild> builds)
    {
        if (run.Game.UnreadableRpks.Count == 0) return;

        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in ContentRoots(run))
        {
            var old = ReadJson<List<EngineBuild>>(Path.Combine(root, EngineBuild.FileName));
            if (old == null) continue;

            foreach (var build in old.Where(b => Touches(b.Source) || b.Parts.Any(p => Touches(p.Source))))
            {
                if (!handled.Add(build.Id)) continue;

                var at = builds.FindIndex(b => b.Id.Equals(build.Id, StringComparison.OrdinalIgnoreCase));
                if (at >= 0) builds[at] = build;
                else builds.Add(build);
            }
        }

        if (handled.Count > 0) Console.WriteLine($"  {handled.Count} engine builds kept as they were: they name an rpk that could not be read");

        bool Touches(string? reference) =>
            reference != null && SlrrGame.TryParseReference(reference, out var rpkPath, out _) && run.Game.IsUnreadable(rpkPath);
    }

    /// <summary>The engines the game offers: the cars' own, the build notes', and the packs' complete kits</summary>
    private static List<EngineBuild> EngineBuilds(Run run)
    {
        var game = run.Game;
        var definitions = run.Definitions;
        var engineBuilds = new SlrrEngineBuilds(game, run.PartIds);
        // Cars get an evaluator of their own: their classes are of no use to the game
        var builds = engineBuilds.FromCars(new SlrrScriptEvaluator(game));
        // The folder was checked when the options were read
        if (run.Options.Notes != null) builds.AddRange(engineBuilds.FromNotes(run.Options.Notes));

        // The packs' own engine kits: what an author wrote as a complete engine. A kit without a block is an
        // upgrade, no engine; one whose parts a car or notes build already lists (with a battery, say) adds nothing
        var kits = engineBuilds.FromKits(run.Scripts, run.Packs.Select(p => p.Rpk).Distinct().ToList());
        var listed = builds.Select(PartSet).ToList();
        kits.RemoveAll(k => !k.Parts.Any(p => p.Part != null && definitions.TryGetValue(p.Part, out var d) && d.BaseClass?.Contains(".block.") == true)
                            || listed.Any(PartSet(k).IsSubsetOf));
        builds.AddRange(kits);
        if (kits.Count > 0) Console.WriteLine($"  {kits.Count} engine kits of the packs are builds");

        static HashSet<string> PartSet(EngineBuild build) =>
            build.Parts.Select(p => p.Part ?? p.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // An engine around a part that was dropped on purpose is no engine the game should offer
        var left = builds.RemoveAll(b => b.Parts.Any(p => p.Part == null && run.Dropped.Contains(p.Source)));
        if (left > 0) Console.WriteLine($"  {left} engine builds left out, they use dropped parts");

        // A build that named a set of carburettors gets one carburettor per pad
        if (run.Multiplicity.Count > 0)
        {
            var repeated = 0;
            foreach (var build in builds)
            {
                for (var i = build.Parts.Count - 1; i >= 0; i--)
                {
                    var part = build.Parts[i];
                    if (part.Part == null || !SlrrGame.TryParseReference(part.Source, out var rpkPath, out var typeId)) continue;
                    var rpk = game.GetRpk(rpkPath);
                    if (rpk == null || !run.Multiplicity.TryGetValue((rpk, typeId), out var count)) continue;

                    for (var extra = 1; extra < count; extra++) build.Parts.Insert(i + 1, part);
                    repeated++;
                }
            }

            if (repeated > 0) Console.WriteLine($"  {repeated} carburettor sets in builds became single carburettors, one per pad");
        }

        Console.WriteLine($"  {builds.Count} engine builds ({builds.Count(b => b.RatedPower != null)} with a rated power, " +
                          $"{builds.Count(b => b.Parts.All(p => p.Part != null))} fully resolved)");
        return builds;
    }

    /// <summary>The totals, and what the run left out, last; a run that left anything out does not exit 0</summary>
    private static int Report(Run run, Stopwatch stopwatch)
    {
        Console.WriteLine();
        Console.WriteLine($"Converted {run.Converted} parts ({run.WithoutModel} without a model) in {stopwatch.Elapsed.TotalSeconds:0.0} s");
        if (run.Failures.Count > 0)
        {
            Console.WriteLine($"{run.Failures.Count} failed:");
            foreach (var failure in run.Failures) Console.WriteLine("  " + failure);
        }

        if (run.Skipped.Count > 0)
        {
            Console.WriteLine($"{run.Skipped.Count} packs skipped:");
            foreach (var skipped in run.Skipped) Console.WriteLine("  " + skipped);
        }

        var unreadable = run.Game.UnreadableRpks;
        if (unreadable.Count > 0)
        {
            // Kept, not removed: an rpk that could not be read has not left SLRR (FindKeptPacks)
            Console.WriteLine($"{unreadable.Count} rpks could not be read: {run.Kept.Count} packs of theirs kept as the last conversion left them, " +
                              "their parts missing where no earlier conversion had them:");
            foreach (var rpk in unreadable) Console.WriteLine("  " + rpk);
        }

        if (run.Failures.Count + run.Skipped.Count + unreadable.Count == 0) return 0;

        Console.WriteLine($"Incomplete: {run.Failures.Count} parts failed, {run.Skipped.Count} packs skipped, {unreadable.Count} rpks unreadable");
        return ExitIncomplete;
    }

    /// <summary>
    /// Prints where a part's slots are against its meshes, as the cfg has them (before any shift): the way to see a
    /// pack's convention for a joint before a fitting lets it meet another pack's
    /// </summary>
    private static void Measure(SlrrGame game, IEnumerable<SourcePart> parts)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var part in parts.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine(part.Id);
            SlrrPartConfig config;
            try
            {
                config = SlrrPartConfig.Load(part.ConfigFile);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  cannot be read: {ex.Message}");
                continue;
            }

            foreach (var (render, meshFile, _) in SelectRenders(game, part, config))
            {
                var min = new System.Numerics.Vector3(float.MaxValue);
                var max = new System.Numerics.Vector3(float.MinValue);
                try
                {
                    foreach (var vertex in SlrrMesh.Load(meshFile).SubMeshes.SelectMany(s => s.Vertices))
                    {
                        var position = System.Numerics.Vector3.Transform(vertex.Position, render.Matrix);
                        min = System.Numerics.Vector3.Min(min, position);
                        max = System.Numerics.Vector3.Max(max, position);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  mesh {Path.GetFileName(meshFile),-40} cannot be read: {ex.Message}");
                    continue;
                }

                Console.WriteLine(string.Format(culture, "  mesh {0,-40} x {1,7:0.000}..{2,7:0.000}  y {3,7:0.000}..{4,7:0.000}  z {5,7:0.000}..{6,7:0.000}",
                    Path.GetFileName(meshFile), min.X, max.X, min.Y, max.Y, min.Z, max.Z));
            }

            foreach (var slot in config.Slots)
            {
                Console.WriteLine(string.Format(culture, "  slot {0,3} {1,-38} ({2,7:0.000}, {3,7:0.000}, {4,7:0.000}){5}",
                    slot.Id, slot.Name, slot.Position.X, slot.Position.Y, slot.Position.Z, slot.AttachesTo.Count > 0 ? "  mounts" : ""));
            }
        }
    }

    internal static Regex Pattern(string glob) =>
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

            // A cfg outside the install counts as missing
            var configFile = game.ContentFile(configPath);
            if (configFile == null) continue;

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

        var scriptFile = game.ContentFile(source.ScriptPath);
        var script = scriptFile != null ? scripts.Evaluate(scriptFile) : null;

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

        var fittings = definitions.Values.SelectMany(d => d.Slots).SelectMany(s => s.Fits.Concat(s.Takes)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
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
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
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
    /// Pairs written by hand (<paramref name="twinRules"/>) override the matcher; null when one names a part
    /// that is not there.
    /// </summary>
    private static SortedDictionary<string, string>? ReplacePack(SlrrGame game, List<SourcePart> oldParts, List<SourcePart> newParts,
        Dictionary<(SlrrRpk, int), string> partIds, Dictionary<string, string?> twinRules, HashSet<string> usedTwinRules)
    {
        // An evaluator of its own: the classes of a pack that is left out are of no use to the game
        var scripts = new SlrrScriptEvaluator(game);
        var geometry = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var newTraits = newParts.Select(Traits).ToList();
        var twins = SlrrPackTwins.Match(oldParts.Select(Traits).ToList(), newTraits);

        var byNewId = newTraits.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
        var ruled = 0;
        foreach (var (oldId, newId) in twinRules)
        {
            var index = twins.FindIndex(t => t.Old.Id.Equals(oldId, StringComparison.OrdinalIgnoreCase));
            if (index < 0) continue;

            SlrrPartTraits? twin = null;
            if (newId != null && !byNewId.TryGetValue(newId, out twin))
            {
                Console.WriteLine($"{TwinOption} names a part that is not there to stand in: {newId}");
                return null;
            }

            twins[index] = twins[index] with { New = twin, Doubt = null };
            usedTwinRules.Add(oldId);
            ruled++;
        }

        var aliases = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (source, twin) in oldParts.Zip(twins))
        {
            if (twin.New == null) partIds.Remove((source.Rpk, source.Entry.TypeId));
            else partIds[(source.Rpk, source.Entry.TypeId)] = aliases[source.Id] = twin.New.Id;
        }

        Console.WriteLine($"  {twins.Count(t => t.New != null)} of {twins.Count} parts have a twin ({twins.Count(t => t.New != null && t.Doubt != null)} in doubt, {ruled} paired by rule)");
        foreach (var twin in twins.Where(t => t.New == null || t.Doubt != null))
        {
            Console.WriteLine(twin.New == null
                ? $"    {twin.Old.Name,-40} no twin ({twin.Old.DisplayName})"
                : $"    {twin.Old.Name,-40} -> {twin.New.Name,-44} {twin.Doubt}");
        }

        return aliases;

        // A part that cannot be read is matched by what is known of it (its id and name): one bad part of a
        // replaced pack must not end the run. It fails again where it is converted, if it is, and is counted there
        SlrrPartTraits Traits(SourcePart source)
        {
            try
            {
                return ReadTraits(source);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    {source.Id}: {ex.Message}; matched by its name alone");
                return new SlrrPartTraits { Id = source.Id, Name = source.Name, Slots = new(), Geometry = new(), Textures = new() };
            }
        }

        SlrrPartTraits ReadTraits(SourcePart source)
        {
            var config = SlrrPartConfig.Load(source.ConfigFile);
            var scriptFile = game.ContentFile(source.ScriptPath);
            var script = scriptFile != null ? scripts.Evaluate(scriptFile) : null;
            var renders = SelectRenders(game, source, config);

            var traits = new SlrrPartTraits
            {
                Id = source.Id,
                Name = source.Name,
                DisplayName = script?.DisplayName,
                BaseClass = script?.BaseClass,
                Slots = config.Slots.Select(s => s.Id).ToHashSet(),
                Geometry = renders.Select(r => GeometryOf(r.MeshFile)).OfType<string>().ToHashSet(),
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

        // A mesh that cannot be read has no shape to match by; the part is matched by the rest
        string? GeometryOf(string meshFile)
        {
            if (geometry.TryGetValue(meshFile, out var hash)) return hash;

            try
            {
                hash = SlrrPackTwins.GeometryHash(SlrrMesh.Load(meshFile));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    {Path.GetFileName(meshFile)}: {ex.Message}; its shape is left out of the match");
                hash = null;
            }

            return geometry[meshFile] = hash;
        }
    }

    /// <summary>
    /// The game runs the part scripts itself, so it gets the classes: everything the evaluation touched plus the
    /// shared part classes
    /// </summary>
    private static HashSet<string> ScriptFiles(SlrrGame game, SlrrScriptEvaluator scripts)
    {
        var files = new HashSet<string>(scripts.UsedClassFiles, StringComparer.OrdinalIgnoreCase);
        var shared = Path.Combine(game.Root, PartsFolder, "scripts");
        if (Directory.Exists(shared)) files.UnionWith(Directory.EnumerateFiles(shared, "*.class", SearchOption.AllDirectories));
        return files;
    }

    /// <summary>
    /// A full run's classes, in the staging folder, for <see cref="Commit"/> to swap in whole for what was there. The
    /// classes of the output's folder that the kept packs' parts use are not among the run's: the old folder's
    /// classes the run did not make are kept with them
    /// </summary>
    private static void StageScripts(Run run, IEnumerable<string> classes)
    {
        var staged = Path.Combine(run.Staging, PartScripts.Folder);
        CopyClasses(run.Game.Root, classes, staged);
        foreach (var root in KeptRoots(run))
        {
            var kept = ConversionOutput.FillMissing(staged, Path.Combine(root, PartScripts.Folder));
            if (kept > 0) Console.WriteLine($"  {kept} script classes of {root} kept for the kept packs");
        }
    }

    /// <summary>Copies the classes in the folder layout class lookup depends on</summary>
    private static void CopyClasses(string gameRoot, IEnumerable<string> files, string destinationRoot)
    {
        var root = Path.GetFullPath(gameRoot);
        foreach (var file in files)
        {
            // Only classes inside the install keep their layout (on another drive the relative path is the file's
            // absolute path, and the copy would land on the file itself)
            if (!PathNames.IsUnder(root, file)) continue;

            var destination = Path.Combine(destinationRoot, Path.GetRelativePath(root, Path.GetFullPath(file)));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
        }
    }

    /// <summary>
    /// Moves a pack's models in from the staging folder: before any pack file names them. Returns the names of the
    /// models it moved, the ones its pack file names
    /// </summary>
    private static HashSet<string> MoveModelsIn(string folder, string staging)
    {
        Directory.CreateDirectory(folder);
        var made = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(staging)) return made;

        foreach (var file in Directory.EnumerateFiles(staging, "*.kn5").ToList())
        {
            var name = Path.GetFileName(file);
            File.Move(file, Path.Combine(folder, name), overwrite: true);
            made.Add(name);
        }

        return made;
    }

    /// <summary>
    /// Writes a pack's definitions once its models are in, then takes away the models of its last conversion that
    /// this one did not make: the pack file never names a model that is not there
    /// </summary>
    private static void WritePack(string folder, string json, HashSet<string> made)
    {
        SafeFile.WriteAllText(Path.Combine(folder, PartPack.FileName), json);

        foreach (var file in Directory.EnumerateFiles(folder, "*.kn5").Where(f => !made.Contains(Path.GetFileName(f))).ToList())
        {
            File.Delete(file);
        }
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

    /// <summary>Whether a pack may be converted into the folder: one it converted before, an empty one or none; nothing else is touched</summary>
    private static bool CanConvertInto(string folder)
    {
        if (!Directory.Exists(folder)) return true;

        var isPack = File.Exists(Path.Combine(folder, PartPack.FileName));
        var isEmpty = !Directory.EnumerateFileSystemEntries(folder).Any();
        return isPack || isEmpty;
    }
}
