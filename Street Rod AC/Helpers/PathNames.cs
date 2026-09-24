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

    /// <summary>
    /// A single folder or file name made from free text (a player's name): invalid characters become '_',
    /// runs of them collapse, and it is trimmed and capped. Never empty.
    /// </summary>
    public static string Sanitize(string? name, int maxLength = 50, string fallback = "unnamed")
    {
        var parts = (name ?? string.Empty).Split(Invalid, StringSplitOptions.RemoveEmptyEntries);
        var joined = string.Join("_", parts).Trim().TrimEnd('.');
        if (joined.Length > maxLength) joined = joined[..maxLength].Trim().TrimEnd('.');
        return joined.Length == 0 || joined is "." or ".." ? fallback : joined;
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
