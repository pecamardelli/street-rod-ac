using System.IO;
using Street_Rod_AC.Helpers;

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
    private readonly List<string> _unreadable = new();
    private readonly HashSet<string> _unreadablePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _outside = new(StringComparer.OrdinalIgnoreCase);

    public SlrrGame(string root)
    {
        Root = root;
    }

    public string Root { get; }

    /// <summary>Rpks that are there but could not be read, with why: they count as missing, and the run reports them</summary>
    public IReadOnlyList<string> UnreadableRpks => _unreadable;

    /// <summary>
    /// Whether the rpk at a path relative to the install (as a pack's source or a reference names it) is there but
    /// could not be read. Not the same as gone: what was converted from it before is kept, not taken for stale
    /// </summary>
    public bool IsUnreadable(string relativePath) => _unreadablePaths.Contains(NormalPath(relativePath));

    private static string NormalPath(string relativePath) => relativePath.Trim().Replace('/', '\\');

    /// <summary>
    /// The rpk at a path relative to the install, loaded once; null when it is missing, lies outside the install, or
    /// cannot be read (a truncated or damaged mod rpk is reported and left out, not the end of the run)
    /// </summary>
    public SlrrRpk? GetRpk(string relativePath)
    {
        if (_rpks.TryGetValue(relativePath, out var cached)) return cached;

        SlrrRpk? rpk = null;
        var fullPath = ContentFile(relativePath);
        if (fullPath != null)
        {
            try
            {
                rpk = SlrrRpk.Load(fullPath, relativePath);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                _unreadable.Add($"{relativePath}: {ex.Message}");
                _unreadablePaths.Add(NormalPath(relativePath));
                Console.WriteLine($"  Skipped {relativePath}: {ex.Message}");
            }
        }

        return _rpks[relativePath] = rpk;
    }

    /// <summary>
    /// Full path of a file that content names by a path relative to the install (an rpk's sourcefile, a native part
    /// cfg, a script, an external rpk); null when the file is missing. A path that leads outside the install (rooted,
    /// another drive, "..") counts as missing and is reported once: a mod must not get files from elsewhere on the
    /// machine embedded into the models and scripts the repo ships.
    /// </summary>
    public string? ContentFile(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return null;

        if (!PathNames.TryCombineUnder(Root, relative, out _))
        {
            if (_outside.Add(relative)) Console.WriteLine($"    {relative}: outside the SLRR folder, taken as missing");
            return null;
        }

        // The path as content spells it under the root: what the converter always used (script and mesh caches key on it)
        var fullPath = Path.Combine(Root, relative);
        return File.Exists(fullPath) ? fullPath : null;
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
        return ContentFile(entry?.FirstValue("sourcefile"));
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
