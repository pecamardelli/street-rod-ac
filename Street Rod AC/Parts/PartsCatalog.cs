using System.IO;
using Newtonsoft.Json;
using Street_Rod_AC.Parts.Scripting;

namespace Street_Rod_AC.Parts;

/// <summary>
/// All converted parts found under a folder, with the attach graph indexed both ways:
/// parts know what they mount on, the catalog answers what can be mounted on a given slot.
/// </summary>
public sealed class PartsCatalog
{
    private const int MaxEquivalents = 16;

    private readonly string _root;
    private readonly Dictionary<string, PartDefinition> _parts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);

    private readonly Lazy<MatingIndex> _mating;

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

    public static PartsCatalog Load(string root)
    {
        var catalog = new PartsCatalog(root);
        if (!Directory.Exists(root)) return catalog;

        foreach (var file in Directory.EnumerateFiles(root, PartPack.FileName, SearchOption.AllDirectories))
        {
            var pack = JsonConvert.DeserializeObject<PartPack>(File.ReadAllText(file));
            if (pack == null) continue;

            foreach (var part in pack.Parts)
            {
                catalog._parts[part.Id] = part;
            }
        }

        var aliasesFile = Path.Combine(root, PartPack.AliasesFileName);
        if (File.Exists(aliasesFile))
        {
            foreach (var (gone, current) in JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(aliasesFile)) ?? new())
            {
                catalog._aliases[gone] = current;
            }
        }

        var buildsFile = Path.Combine(root, EngineBuild.FileName);
        if (File.Exists(buildsFile))
            catalog.EngineBuilds = JsonConvert.DeserializeObject<List<EngineBuild>>(File.ReadAllText(buildsFile)) ?? new List<EngineBuild>();

        return catalog;
    }

    /// <summary>A part by its id, or by the id it had in a pack that has since been replaced</summary>
    public PartDefinition? Get(string id) => _parts.GetValueOrDefault(id) ?? _parts.GetValueOrDefault(CurrentId(id));

    /// <summary>True when parts have changed ids: saves made before may hold the old ones</summary>
    public bool HasAliases => _aliases.Count > 0;

    /// <summary>The id a part goes by now: its own, unless the pack it came from was replaced by a later release</summary>
    public string CurrentId(string id) => !_parts.ContainsKey(id) && _aliases.TryGetValue(id, out var current) ? current : id;

    /// <summary>
    /// Whether two slots go together. A slot names the slots it attaches to, on either side of the joint (a header
    /// names the head it bolts to, a block names the radiator it takes), and a slot may stand in for the slot of
    /// another part: whatever fits there fits here.
    /// </summary>
    public bool CanMate(PartDefinition part, PartSlot slot, PartDefinition other, PartSlot otherSlot)
    {
        var mine = Equivalents(part, slot);
        var theirs = Equivalents(other, otherSlot);
        return Names(mine, theirs) || Names(theirs, mine);

        static bool Names(List<(PartDefinition Part, PartSlot Slot)> from, List<(PartDefinition Part, PartSlot Slot)> to) =>
            from.Any(f => f.Slot.AttachesTo.Any(a => a.Part != null && to.Any(t =>
                t.Slot.Id == a.Slot && t.Part.Id.Equals(a.Part, StringComparison.OrdinalIgnoreCase))));
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
                    }
                }
            }
        }

        private static void Add(Dictionary<(string, int), List<(PartDefinition, PartSlot)>> map, (string, int) key, (PartDefinition, PartSlot) value)
        {
            if (!map.TryGetValue(key, out var list)) map[key] = list = new();
            list.Add(value);
        }
    }

    /// <summary>The slot itself and every slot it stands in for, directly or through another stand-in</summary>
    private List<(PartDefinition Part, PartSlot Slot)> Equivalents(PartDefinition part, PartSlot slot)
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
    public string? GetModelPath(PartDefinition part)
    {
        if (part.Model == null) return null;

        var packId = part.Id[..part.Id.LastIndexOf('/')];
        return Path.Combine(_root, packId.Replace('/', Path.DirectorySeparatorChar), part.Model);
    }
}
