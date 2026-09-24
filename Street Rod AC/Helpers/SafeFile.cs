using System.IO;
using System.Text;

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

    public static void WriteAllText(string path, string contents, Encoding? encoding = null) =>
        WriteAllBytes(path, (encoding ?? Utf8NoBom).GetBytes(contents));

    public static void WriteAllBytes(string path, byte[] contents)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);

        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(contents, 0, contents.Length);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }
}
