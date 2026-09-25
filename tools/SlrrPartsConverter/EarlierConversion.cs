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

    /// <summary>Null when the folder holds no converted pack, or a file of it does not read (said on the console)</summary>
    public static EarlierConversion? Load(string folder)
    {
        if (!Directory.Exists(folder)) return null;

        var parts = new List<(string, string[], int)>();
        foreach (var file in Directory.EnumerateFiles(folder, PartPack.FileName, SearchOption.AllDirectories))
        {
            if (!TryRead<PartPack>(file, out var pack)) return null;
            if (pack == null) continue;

            // A hand-edited pack may say null where a list or a name goes
            var rpks = (pack.Source ?? string.Empty).Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            parts.AddRange((pack.Parts ?? new()).Where(p => p != null && !string.IsNullOrWhiteSpace(p.Id)).Select(p => (p.Id, rpks, p.SourceTypeId)));
        }

        if (parts.Count == 0) return null;

        var aliasesFile = Path.Combine(folder, PartPack.AliasesFileName);
        Dictionary<string, string>? aliases = null;
        if (File.Exists(aliasesFile) && !TryRead(aliasesFile, out aliases)) return null;

        var kept = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (gone, current) in aliases ?? new())
        {
            if (!string.IsNullOrWhiteSpace(current)) kept[gone] = current;
        }

        return new EarlierConversion(parts, kept);
    }

    /// <summary>A JSON file of the earlier run; false (and the file named on the console) when it does not read</summary>
    private static bool TryRead<T>(string file, out T? value) where T : class
    {
        try
        {
            value = JsonConvert.DeserializeObject<T>(File.ReadAllText(file));
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"{file} could not be read: {ex.Message}");
            value = null;
            return false;
        }
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
