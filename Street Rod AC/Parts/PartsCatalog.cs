using System.Collections.Concurrent;
using System.IO;
using Newtonsoft.Json;
using Street_Rod_AC.Helpers;
using Street_Rod_AC.Parts.Scripting;

namespace Street_Rod_AC.Parts;

/// <summary>
/// All converted parts found under a folder, with the attach graph indexed both ways:
/// parts know what they mount on, the catalog answers what can be mounted on a given slot.
/// </summary>
public sealed class PartsCatalog
{
    private const int MaxEquivalents = 16;
    private const int MaxAliasHops = 8;

    private readonly string _root;
    private readonly Dictionary<string, PartDefinition> _parts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);

    private readonly Lazy<MatingIndex> _mating;

    // A slot and those it stands in for: the graph does not change once loaded, and CanMate asks in nested loops
    private readonly ConcurrentDictionary<(PartDefinition Part, PartSlot Slot), List<(PartDefinition Part, PartSlot Slot)>> _equivalents = new();

    private readonly List<string> _problems = new();

    private PartsCatalog(string root)
    {
        _root = root;
        Scripts = new ScriptClassLoader(Path.Combine(root, PartScripts.Folder));
        _mating = new Lazy<MatingIndex>(() => new MatingIndex(this));
    }

    public IReadOnlyDictionary<string, PartDefinition> Parts => _parts;

    /// <summary>Folder the catalog was loaded from</summary>
    public string Root => _root;

    /// <summary>
    /// The compiled scripts of the parts. One loader for the catalog: a class is read and parsed once,
    /// however many times an assembly of parts is brought to life.
    /// </summary>
    public ScriptClassLoader Scripts { get; }

    /// <summary>Complete engines as part lists: factory builds of the source game's cars and written-down builds</summary>
    public IReadOnlyList<EngineBuild> EngineBuilds { get; private set; } = Array.Empty<EngineBuild>();

    /// <summary>A catalog without parts, for when the parts under the folder cannot be read</summary>
    public static PartsCatalog Empty(string root) => new(root);

    /// <summary>What could not be read, or was left out and why: one line each, for the log</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <remarks>
    /// A pack that does not read is a pack that is not there, and a part without an id is no part: each is one line
    /// in <see cref="Problems"/>, and the rest of the catalog stands (one bad pack.json used to cost every car its parts).
    /// </remarks>
    public static PartsCatalog Load(string root)
    {
        var catalog = new PartsCatalog(root);
        if (!Directory.Exists(root)) return catalog;

        // Which pack each part came from, to tell when a later one has it too
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(root, PartPack.FileName, SearchOption.AllDirectories))
        {
            var packName = Path.GetRelativePath(root, file);
            PartPack? pack;
            try
            {
                pack = JsonConvert.DeserializeObject<PartPack>(File.ReadAllText(file));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                catalog._problems.Add($"{packName}: {ex.Message}; its parts are left out");
                continue;
            }

            if (pack?.Parts == null) continue;

            foreach (var part in pack.Parts)
            {
                if (part == null || string.IsNullOrWhiteSpace(part.Id))
                {
                    catalog._problems.Add($"{packName}: a part without an id is left out");
                    continue;
                }

                if (owners.TryGetValue(part.Id, out var earlier))
                    catalog._problems.Add($"{part.Id} is in {earlier} and in {packName}: the one in {packName} is used");

                owners[part.Id] = packName;
                catalog._parts[part.Id] = part;
            }
        }

        var aliasesFile = Path.Combine(root, PartPack.AliasesFileName);
        try
        {
            if (File.Exists(aliasesFile))
            {
                foreach (var (gone, current) in JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(aliasesFile)) ?? new())
                {
                    // An alias to nothing would leave a saved part with no id at all
                    if (string.IsNullOrWhiteSpace(current))
                    {
                        catalog._problems.Add($"{PartPack.AliasesFileName}: {gone} leads nowhere; left out");
                        continue;
                    }

                    catalog._aliases[gone] = current;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            catalog._problems.Add($"{PartPack.AliasesFileName}: {ex.Message}; parts that changed ids are not found by their old ones");
        }

        var buildsFile = Path.Combine(root, EngineBuild.FileName);
        try
        {
            if (File.Exists(buildsFile))
            {
                // A build without an id can't be found again (cars and saves name builds by it): left out, the rest stand
                var builds = JsonConvert.DeserializeObject<List<EngineBuild?>>(File.ReadAllText(buildsFile)) ?? new();
                var named = builds.Where(b => b != null && !string.IsNullOrWhiteSpace(b.Id)).Select(b => b!).ToList();
                if (named.Count < builds.Count)
                    catalog._problems.Add($"{EngineBuild.FileName}: {builds.Count - named.Count} build(s) without an id are left out");
                catalog.EngineBuilds = named;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            catalog._problems.Add($"{EngineBuild.FileName}: {ex.Message}; no engine builds");
        }

        // Slots nudged into place in the garage since the last conversion
        catalog.Shifts = SlotShifts.Load(root);
        if (catalog.Shifts.Problem != null) catalog._problems.Add(catalog.Shifts.Problem);
        catalog.Shifts.ApplyTo(catalog._parts);

        return catalog;
    }

    /// <summary>Slots moved after conversion (the garage's placement mode); already applied to the parts</summary>
    public SlotShifts Shifts { get; private set; } = new();

    /// <summary>
    /// Moves a slot in its part's space, for good: the part is changed in place (every slot of that id, as a
    /// loaded shift does: a cfg may declare an id twice) and the move is written next to the packs, to be folded
    /// into the packs by the next conversion.
    /// </summary>
    /// <returns>False when the part moved but the move could not be written</returns>
    public bool ShiftSlot(PartDefinition part, int slotId, float[] delta)
    {
        foreach (var slot in part.Slots.Where(s => s.Id == slotId))
        {
            for (var axis = 0; axis < 3; axis++) slot.Position[axis] += delta[axis];
        }

        return Shifts.Add(part.Id, slotId, delta);
    }

    /// <summary>A part by its id, or by the id it had in a pack that has since been replaced</summary>
    public PartDefinition? Get(string id) => _parts.GetValueOrDefault(id) ?? _parts.GetValueOrDefault(CurrentId(id));

    /// <summary>True when parts have changed ids: saves made before may hold the old ones</summary>
    public bool HasAliases => _aliases.Count > 0;

    /// <summary>
    /// The id a part goes by now: its own, unless the pack it came from was renamed or replaced by a later release
    /// since. Aliases chain when both happened (a pack renamed, then replaced under its new name), so they are
    /// followed until an id that is a part, or as far as they go.
    /// </summary>
    public string CurrentId(string id)
    {
        for (var hops = 0; !_parts.ContainsKey(id) && hops < MaxAliasHops && _aliases.TryGetValue(id, out var current); hops++)
        {
            id = current;
        }

        return id;
    }

    /// <summary>
    /// Whether two slots go together. A slot names the slots it attaches to, on either side of the joint (a header
    /// names the head it bolts to, a block names the radiator it takes), and a slot may stand in for the slot of
    /// another part: whatever fits there fits here. Slots also go together by a standard fitting: a carburettor
    /// base that fits "carb:4bbl" goes on any manifold pad that takes it, whatever pack either is from.
    /// </summary>
    public bool CanMate(PartDefinition part, PartSlot slot, PartDefinition other, PartSlot otherSlot)
    {
        var mine = Equivalents(part, slot);
        var theirs = Equivalents(other, otherSlot);
        return Names(mine, theirs) || Names(theirs, mine) || Fits(mine, theirs) || Fits(theirs, mine);

        static bool Names(List<(PartDefinition Part, PartSlot Slot)> from, List<(PartDefinition Part, PartSlot Slot)> to) =>
            from.Any(f => f.Slot.AttachesTo.Any(a => a.Part != null && to.Any(t =>
                t.Slot.Id == a.Slot && t.Part.Id.Equals(a.Part, StringComparison.OrdinalIgnoreCase))));

        static bool Fits(List<(PartDefinition Part, PartSlot Slot)> from, List<(PartDefinition Part, PartSlot Slot)> to) =>
            from.Any(f => f.Slot.Fits.Any(fitting => to.Any(t => t.Slot.Takes.Contains(fitting, StringComparer.OrdinalIgnoreCase))));
    }

    /// <summary>
    /// Every part that goes on a slot, with the slot of its own it goes on by: the same answer as trying
    /// <see cref="CanMate"/> against the whole catalog. Part configs copy attach lines between siblings, so a
    /// part of the parent's own kind shows up now and then (a block "on" another block's alternator slot);
    /// those are left out.
    /// </summary>
    public IReadOnlyList<(PartDefinition Part, PartSlot Slot)> FindMountable(PartDefinition parent, PartSlot slot)
    {
        var index = _mating.Value;
        var parentGroup = PartKinds.GroupOf(parent);
        var result = new List<(PartDefinition Part, PartSlot Slot)>();
        var seen = new HashSet<PartSlot>();

        foreach (var equivalent in Equivalents(parent, slot))
        {
            Add(index.NamedBy.GetValueOrDefault(Key(equivalent.Part.Id, equivalent.Slot.Id)));
            foreach (var target in equivalent.Slot.AttachesTo)
            {
                if (target.Part != null) Add(index.StandIns.GetValueOrDefault(Key(target.Part, target.Slot)));
            }

            foreach (var fitting in equivalent.Slot.Takes) Add(index.FittedBy.GetValueOrDefault(fitting));
        }

        return result;

        void Add(List<(PartDefinition Part, PartSlot Slot)>? candidates)
        {
            if (candidates == null) return;

            foreach (var candidate in candidates)
            {
                if (ReferenceEquals(candidate.Part, parent) || !seen.Add(candidate.Slot)) continue;
                if (PartKinds.IsBlock(candidate.Part)) continue;
                if (PartKinds.NeverStacks(parentGroup) && PartKinds.GroupOf(candidate.Part) == parentGroup) continue;

                result.Add(candidate);
            }
        }
    }

    private static (string PartId, int SlotId) Key(string partId, int slotId) => (partId.ToLowerInvariant(), slotId);

    /// <summary><see cref="CanMate"/> turned around: from a slot to the slots that mate with it</summary>
    private sealed class MatingIndex
    {
        /// <summary>Slots that name a slot in their attach lines, themselves or through a slot they stand in for</summary>
        public Dictionary<(string PartId, int SlotId), List<(PartDefinition Part, PartSlot Slot)>> NamedBy { get; } = new();

        /// <summary>Slots that stand in for a slot, the slot itself included</summary>
        public Dictionary<(string PartId, int SlotId), List<(PartDefinition Part, PartSlot Slot)>> StandIns { get; } = new();

        /// <summary>Slots that mount by a standard fitting, themselves or through a slot they stand in for</summary>
        public Dictionary<string, List<(PartDefinition Part, PartSlot Slot)>> FittedBy { get; } = new(StringComparer.OrdinalIgnoreCase);

        public MatingIndex(PartsCatalog catalog)
        {
            foreach (var part in catalog._parts.Values)
            {
                foreach (var slot in part.Slots)
                {
                    foreach (var equivalent in catalog.Equivalents(part, slot))
                    {
                        Add(StandIns, Key(equivalent.Part.Id, equivalent.Slot.Id), (part, slot));
                        foreach (var target in equivalent.Slot.AttachesTo)
                        {
                            if (target.Part != null) Add(NamedBy, Key(target.Part, target.Slot), (part, slot));
                        }

                        foreach (var fitting in equivalent.Slot.Fits) Add(FittedBy, fitting, (part, slot));
                    }
                }
            }
        }

        private static void Add<TKey>(Dictionary<TKey, List<(PartDefinition, PartSlot)>> map, TKey key, (PartDefinition, PartSlot) value)
            where TKey : notnull
        {
            if (!map.TryGetValue(key, out var list)) map[key] = list = new();
            list.Add(value);
        }
    }

    /// <summary>The slot itself and every slot it stands in for, directly or through another stand-in</summary>
    private List<(PartDefinition Part, PartSlot Slot)> Equivalents(PartDefinition part, PartSlot slot) =>
        _equivalents.GetOrAdd((part, slot), key => FindEquivalents(key.Part, key.Slot));

    private List<(PartDefinition Part, PartSlot Slot)> FindEquivalents(PartDefinition part, PartSlot slot)
    {
        var result = new List<(PartDefinition Part, PartSlot Slot)> { (part, slot) };
        for (var i = 0; i < result.Count && result.Count < MaxEquivalents; i++)
        {
            foreach (var compatible in result[i].Slot.CompatibleWith)
            {
                if (compatible.Part == null || Get(compatible.Part) is not { } target) continue;

                var targetSlot = target.Slots.FirstOrDefault(s => s.Id == compatible.Slot);
                if (targetSlot != null && !result.Any(r => ReferenceEquals(r.Slot, targetSlot))) result.Add((target, targetSlot));
            }
        }

        return result;
    }

    /// <summary>Full path of the part's model, null if it has none</summary>
    /// <remarks>
    /// The id and the model name are pack.json content: an id with no pack in it has no folder to look in, and a
    /// path that leads out of the parts folder is no model
    /// </remarks>
    public string? GetModelPath(PartDefinition part)
    {
        if (part.Model == null) return null;

        var packEnd = part.Id.LastIndexOf('/');
        if (packEnd <= 0) return null;

        var relative = Path.Combine(part.Id[..packEnd].Replace('/', Path.DirectorySeparatorChar), part.Model);
        return PathNames.TryCombineUnder(_root, relative, out var path) ? path : null;
    }
}
