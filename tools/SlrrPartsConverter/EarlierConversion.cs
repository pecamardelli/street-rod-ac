using Newtonsoft.Json;
using Street_Rod_AC.Parts;
using Street_Rod_AC.Slrr;

namespace Street_Rod_AC;

/// <summary>
/// The content an earlier run made (normally what the game runs on): what its parts were converted from, and the
/// ids it answered for saves made before it. A release of a mod that renames its files keeps the rpk's resource
/// ids (the SLRR game knows parts by nothing else), so a part that is gone by name is found again by its resource.
/// </summary>
public sealed class EarlierConversion
{
    /// <summary>As many hops as the game follows (PartsCatalog.CurrentId)</summary>
    private const int MaxHops = 8;

    /// <summary>A pack fed by several rpks names them all; the type id is unique within one rpk</summary>
    private readonly List<(string Id, string[] Rpks, int TypeId)> _parts;

    private readonly Dictionary<string, string> _aliases;

    private EarlierConversion(List<(string, string[], int)> parts, Dictionary<string, string> aliases)
    {
        _parts = parts;
        _aliases = aliases;
    }

    /// <summary>Null when the folder holds no converted pack</summary>
    public static EarlierConversion? Load(string folder)
    {
        if (!Directory.Exists(folder)) return null;

        var parts = new List<(string, string[], int)>();
        foreach (var file in Directory.EnumerateFiles(folder, PartPack.FileName, SearchOption.AllDirectories))
        {
            var pack = JsonConvert.DeserializeObject<PartPack>(File.ReadAllText(file));
            if (pack == null) continue;

            var rpks = pack.Source.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            parts.AddRange(pack.Parts.Select(p => (p.Id, rpks, p.SourceTypeId)));
        }

        if (parts.Count == 0) return null;

        var aliasesFile = Path.Combine(folder, PartPack.AliasesFileName);
        var aliases = File.Exists(aliasesFile)
            ? JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(aliasesFile))
            : null;
        return new EarlierConversion(parts, new Dictionary<string, string>(aliases ?? new(), StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Adds to the aliases the ids of this conversion that the current one no longer answers: a part whose rpk
    /// resource is converted now under another name points at that part, and an older alias is kept while it
    /// still leads to a part. Returns how many of each were added.
    /// </summary>
    public (int Renamed, int Kept) Carry(SlrrGame game, Dictionary<(SlrrRpk, int), string> partIds,
        Dictionary<string, PartDefinition> current, SortedDictionary<string, string> aliases)
    {
        var renamed = 0;
        foreach (var (id, rpks, typeId) in _parts)
        {
            if (current.ContainsKey(id) || aliases.ContainsKey(id)) continue;

            // The one part the resource is now; two rpks of the pack answering differently is nobody's part
            var now = rpks.Select(game.GetRpk).Where(rpk => rpk != null)
                .Select(rpk => partIds.GetValueOrDefault((rpk!, typeId))).Where(found => found != null)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (now.Count != 1 || !current.ContainsKey(now[0]!)) continue;

            aliases[id] = now[0]!;
            renamed++;
        }

        // After the renamed parts: an older alias may lead to one of them
        var kept = 0;
        foreach (var (gone, target) in _aliases)
        {
            if (current.ContainsKey(gone) || aliases.ContainsKey(gone) || !Leads(target)) continue;

            aliases[gone] = target;
            kept++;
        }

        return (renamed, kept);

        // Through the aliases as they end up: the game follows chains too
        bool Leads(string id)
        {
            for (var hops = 0; hops < MaxHops; hops++)
            {
                if (current.ContainsKey(id)) return true;
                if (!aliases.TryGetValue(id, out var next)) return false;

                id = next;
            }

            return false;
        }
    }
}
