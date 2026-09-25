using System.IO;

namespace Street_Rod_AC.Helpers;

/// <summary>
/// Names that come from content (car ids, part ids, save names, paths inside mod files) turned into paths
/// that cannot leave the folder they are meant for.
///
/// Plain BCL on purpose: the tools compile it by link.
/// </summary>
public static class PathNames
{
    private static readonly char[] Invalid = Path.GetInvalidFileNameChars();

    // Windows' device names: a file called any of these, whatever its extension, opens the device instead
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// A single folder or file name made from free text (a player's name): invalid characters become '_',
    /// runs of them collapse, and it is trimmed and capped. A device name (a player called "Con") gets a '_'
    /// after it. Never empty.
    /// </summary>
    public static string Sanitize(string? name, int maxLength = 50, string fallback = "unnamed")
    {
        var parts = (name ?? string.Empty).Split(Invalid, StringSplitOptions.RemoveEmptyEntries);
        var joined = TrimEnds(string.Join("_", parts));
        if (joined.Length > maxLength) joined = TrimEnds(joined[..maxLength]);
        if (joined.Length == 0 || joined is "." or "..") return fallback;

        // Windows ignores what follows the first dot, and spaces before it, when it looks for a device
        var dot = joined.IndexOf('.');
        var stem = dot < 0 ? joined : joined[..dot];
        return Reserved.Contains(stem.TrimEnd()) ? stem.TrimEnd() + "_" + (dot < 0 ? "" : joined[dot..]) : joined;
    }

    /// <summary>No spaces at either end and no dots at the end, however they are mixed ("abc . ." is "abc")</summary>
    private static string TrimEnds(string text)
    {
        var trimmed = text.Trim().TrimEnd('.');
        while (trimmed.Length != text.Length)
        {
            text = trimmed;
            trimmed = text.Trim().TrimEnd('.');
        }

        return trimmed;
    }

    /// <summary>
    /// True when the name is usable as one path segment as it is: not empty, not "." or "..", no separators,
    /// no invalid characters, not rooted. For ids that should already be clean (car folder names).
    /// </summary>
    public static bool IsSafeSegment(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name is not "." and not ".."
        && name.IndexOfAny(Invalid) < 0
        && name.IndexOf(Path.DirectorySeparatorChar) < 0
        && name.IndexOf(Path.AltDirectorySeparatorChar) < 0
        && !Path.IsPathRooted(name);

    /// <summary>
    /// <paramref name="root"/> joined with a relative path from content, resolved; false when the result would
    /// be the root itself or anywhere outside it (a rooted path, a drive, "..").
    /// </summary>
    public static bool TryCombineUnder(string root, string? relative, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) return false;

        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!IsUnder(fullRoot, candidate)) return false;

        fullPath = candidate;
        return true;
    }

    /// <summary>
    /// <see cref="TryCombineUnder"/> that throws: for code that must never touch anything outside the root
    /// (recursive deletes).
    /// </summary>
    public static string CombineUnder(string root, string relative) =>
        TryCombineUnder(root, relative, out var full)
            ? full
            : throw new InvalidOperationException($"'{relative}' does not stay inside '{root}'");

    /// <summary>True when <paramref name="path"/> is strictly inside <paramref name="root"/> (both resolved)</summary>
    public static bool IsUnder(string root, string path)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) && fullPath.Length > fullRoot.Length;
    }
}
