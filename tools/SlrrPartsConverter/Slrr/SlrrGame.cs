using System.IO;

namespace Street_Rod_AC.Slrr;

/// <summary>
/// Read-only view over a Street Legal Racing: Redline install: loads rpk files on demand
/// and follows resource ids across them.
/// </summary>
public sealed class SlrrGame
{
    private const int MaxCategoryDepth = 12;

    // Roots of the type tree, shared by every part and therefore not worth keeping
    private static readonly HashSet<string> GenericCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "part", "parts", "typ_game_object", "und"
    };

    private readonly Dictionary<string, SlrrRpk?> _rpks = new(StringComparer.OrdinalIgnoreCase);

    public SlrrGame(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public SlrrRpk? GetRpk(string relativePath)
    {
        if (_rpks.TryGetValue(relativePath, out var cached)) return cached;

        var fullPath = Path.Combine(Root, relativePath);
        return _rpks[relativePath] = File.Exists(fullPath) ? SlrrRpk.Load(fullPath, relativePath) : null;
    }

    /// <summary>Follows an id to its entry, hopping to an external rpk when the high word says so</summary>
    public (SlrrRpk? Rpk, SlrrRpkEntry? Entry) Resolve(SlrrRpk rpk, int id)
    {
        if (id < 0) return (null, null);

        var externalIndex = id >> 16;
        if (externalIndex != 0)
        {
            if (!rpk.Externals.TryGetValue(externalIndex, out var externalPath)) return (null, null);

            var external = GetRpk(externalPath);
            if (external == null) return (null, null);

            rpk = external;
            id &= 0xFFFF;
        }

        return rpk.Entries.TryGetValue(id, out var entry) ? (rpk, entry) : (null, null);
    }

    /// <summary>"rpk path#0xID" for an id as seen from <paramref name="rpk"/>, resolvable or not</summary>
    public static string Describe(SlrrRpk rpk, int id)
    {
        var externalIndex = id >> 16;
        var path = externalIndex == 0
            ? rpk.RelativePath
            : rpk.Externals.TryGetValue(externalIndex, out var external) ? external : $"?{externalIndex}";
        return $"{path}#0x{id & 0xFFFF:X4}";
    }

    /// <summary><see cref="Describe"/> read back: the rpk path and type id of an "rpk path#0xID" reference</summary>
    public static bool TryParseReference(string reference, out string rpkPath, out int typeId)
    {
        rpkPath = string.Empty;
        typeId = 0;
        var hash = reference.LastIndexOf("#0x", StringComparison.Ordinal);
        if (hash < 0 || !int.TryParse(reference[(hash + 3)..], System.Globalization.NumberStyles.HexNumber, null, out typeId)) return false;

        rpkPath = reference[..hash];
        return true;
    }

    /// <summary>Full path of the file behind a mesh/texture/sound id, null if the id or the file is missing</summary>
    public string? SourceFile(SlrrRpk rpk, int id)
    {
        var (_, entry) = Resolve(rpk, id);
        var relative = entry?.FirstValue("sourcefile");
        if (relative == null) return null;

        var fullPath = Path.Combine(Root, relative);
        return File.Exists(fullPath) ? fullPath : null;
    }

    /// <summary>Names of the types above an entry, nearest first, e.g. blocks, Main, engine</summary>
    public List<string> Categories(SlrrRpk rpk, SlrrRpkEntry entry)
    {
        var result = new List<string>();
        var current = (Rpk: rpk, Entry: entry);

        for (var depth = 0; depth < MaxCategoryDepth; depth++)
        {
            var (parentRpk, parent) = Resolve(current.Rpk, current.Entry.SuperId);
            if (parentRpk == null || parent == null || ReferenceEquals(parent, current.Entry)) break;

            if (!GenericCategories.Contains(parent.Alias)) result.Add(parent.Alias);
            current = (parentRpk, parent);
        }

        return result;
    }
}
