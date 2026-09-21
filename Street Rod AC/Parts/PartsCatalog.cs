using System.IO;
using Newtonsoft.Json;

namespace Street_Rod_AC.Parts;

/// <summary>
/// All converted parts found under a folder, with the attach graph indexed both ways:
/// parts know what they mount on, the catalog answers what can be mounted on a given slot.
/// </summary>
public sealed class PartsCatalog
{
    private readonly string _root;
    private readonly Dictionary<string, PartDefinition> _parts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string PartId, int SlotId), List<(PartDefinition Part, PartSlot Slot)>> _mountable = new();

    private PartsCatalog(string root)
    {
        _root = root;
    }

    public IReadOnlyDictionary<string, PartDefinition> Parts => _parts;

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

        foreach (var part in catalog._parts.Values)
        {
            foreach (var slot in part.Slots)
            {
                foreach (var target in slot.AttachesTo)
                {
                    if (target.Part == null) continue;

                    var key = (target.Part.ToLowerInvariant(), target.Slot);
                    if (!catalog._mountable.TryGetValue(key, out var list)) catalog._mountable[key] = list = new();
                    list.Add((part, slot));
                }
            }
        }

        return catalog;
    }

    public PartDefinition? Get(string id) => _parts.GetValueOrDefault(id);

    /// <summary>Parts that can be mounted on a slot, each with the slot of its own it mounts by</summary>
    public IReadOnlyList<(PartDefinition Part, PartSlot Slot)> GetMountable(PartDefinition parent, PartSlot slot) =>
        _mountable.TryGetValue((parent.Id.ToLowerInvariant(), slot.Id), out var list) ? list : Array.Empty<(PartDefinition, PartSlot)>();

    /// <summary>Full path of the part's model, null if it has none</summary>
    public string? GetModelPath(PartDefinition part)
    {
        if (part.Model == null) return null;

        var packId = part.Id[..part.Id.LastIndexOf('/')];
        return Path.Combine(_root, packId.Replace('/', Path.DirectorySeparatorChar), part.Model);
    }
}
