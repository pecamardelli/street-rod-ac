using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Street_Rod_AC.Helpers;

/// <summary>
/// Writes a file so that a crash, a kill or the power going mid-write leaves either the old file or the new one,
/// never a truncated one: the content goes to a temp file next to the target, is flushed to the disk, and then
/// takes the target's place in one rename (same folder, so same volume, so the rename is atomic on NTFS).
///
/// Plain BCL on purpose: the tools compile it by link.
/// </summary>
public static class SafeFile
{
    /// <summary>UTF-8 without the byte-order mark: what AC's INI files and our JSON should be written as</summary>
    public static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Tries at the rename: a reader holding the target without delete sharing (AC, an indexer, a scanner) lets go soon</summary>
    private const int MoveAttempts = 5;

    private const string TempExtension = ".tmp";

    public static void WriteAllText(string path, string contents, Encoding? encoding = null) =>
        WriteAllBytes(path, (encoding ?? Utf8NoBom).GetBytes(contents));

    public static void WriteAllBytes(string path, byte[] contents)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);

        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}{TempExtension}");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(contents, 0, contents.Length);
                stream.Flush(flushToDisk: true);
            }

            MoveIntoPlace(temp, path);
        }
        catch
        {
            try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>
    /// The rename over the target needs it free of readers that do not share delete; one that holds it for a moment
    /// (Content Manager reading race.ini, Defender) gets a few short tries before the write fails
    /// </summary>
    private static void MoveIntoPlace(string temp, string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < MoveAttempts)
            {
                Thread.Sleep(Math.Min(50 * attempt, 100));
            }
        }
    }

    /// <summary>
    /// Deletes the temp files a write cut short (a kill or the power going between the temp file and the rename) left
    /// in <paramref name="folder"/>: only files named the way this class names them, and only ones older than a minute,
    /// so a write under way is never touched. Returns how many went; never throws.
    /// </summary>
    public static int SweepTemps(string folder, bool recursive = false)
    {
        var removed = 0;
        try
        {
            if (!Directory.Exists(folder)) return 0;

            var cutOff = DateTime.UtcNow - TimeSpan.FromMinutes(1);
            var files = Directory.EnumerateFiles(folder, "." + "*" + TempExtension,
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                try
                {
                    if (!IsTempName(Path.GetFileName(file)) || File.GetLastWriteTimeUtc(file) > cutOff) continue;
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                    removed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Held by someone right now: the next start tries again
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be listed has nothing we can clean
        }

        return removed;
    }

    /// <summary>".{name}.{32 hex digits}.tmp", as <see cref="WriteAllBytes"/> names its temp files</summary>
    internal static bool IsTempName(string fileName)
    {
        if (!fileName.StartsWith('.') || !fileName.EndsWith(TempExtension, StringComparison.OrdinalIgnoreCase)) return false;

        var stem = fileName[..^TempExtension.Length];
        var dot = stem.LastIndexOf('.');
        if (dot < 2) return false; // at least ".x" before the GUID

        var guid = stem[(dot + 1)..];
        return guid.Length == 32 && guid.All(Uri.IsHexDigit);
    }
}
