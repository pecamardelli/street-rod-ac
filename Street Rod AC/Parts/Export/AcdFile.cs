using System.Globalization;
using System.IO;
using System.Text;

namespace Street_Rod_AC.Parts.Export;

/// <summary>
/// Reads a car's packed data (data.acd). The entries are the files of the data folder, each byte stored in
/// its own 32-bit word, shifted by a key that is worked out from the car's folder name. The key derivation is
/// the game's own; it was checked against every packed car of the two installs on this machine.
/// </summary>
public static class AcdFile
{
    public const string FileName = "data.acd";

    private const int Marker = -1111;

    /// <returns>File name to content, in the order of the archive</returns>
    /// <exception cref="InvalidDataException">The file is not a data.acd, or is cut short</exception>
    public static List<(string Name, byte[] Content)> Read(string acdPath)
    {
        var entries = new List<(string, byte[])>();
        Walk(acdPath, _ => true, (name, content) => entries.Add((name, content)));
        return entries;
    }

    /// <summary>
    /// One file of the archive, found by name (as the game does, case aside) without decoding the others: a car's
    /// data.acd holds its whole data folder, and a reader after one figure should not pay for the rest.
    /// </summary>
    /// <returns>The content, null when the archive has no such file; of the last one, when it has the name twice (as <see cref="AcCarData"/> has it)</returns>
    /// <exception cref="InvalidDataException">The file is not a data.acd, or is cut short</exception>
    public static byte[]? ReadEntry(string acdPath, string name)
    {
        byte[]? found = null;
        Walk(acdPath, entry => entry.Equals(name, StringComparison.OrdinalIgnoreCase), (_, content) => found = content);
        return found;
    }

    /// <summary>
    /// Goes through the entries in order. The lengths are the car mod's, third-party data: one that is negative or
    /// runs past the end is a broken archive, never an index out of range.
    /// </summary>
    /// <param name="wanted">Whether to decode the entry with this name</param>
    /// <param name="decoded">Gets the name and content of each entry that was wanted</param>
    private static void Walk(string acdPath, Func<string, bool> wanted, Action<string, byte[]> decoded)
    {
        var key = Encoding.ASCII.GetBytes(KeyFor(Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(acdPath))) ?? string.Empty));
        var data = File.ReadAllBytes(acdPath);

        var position = 0;
        if (data.Length >= 8 && BitConverter.ToInt32(data, 0) == Marker) position = 8;

        for (var index = 0; position + 4 <= data.Length; index++)
        {
            var nameLength = BitConverter.ToInt32(data, position);
            position += 4;
            if (nameLength < 0 || nameLength > data.Length - position)
                throw new InvalidDataException($"{acdPath}: entry {index} has a name {nameLength} bytes long, past the end of the file");
            var name = Encoding.Latin1.GetString(data, position, nameLength);
            position += nameLength;

            if (position + 4 > data.Length) throw new InvalidDataException($"{acdPath}: entry {name} is cut short");
            var size = BitConverter.ToInt32(data, position);
            position += 4;
            if (size < 0 || position + size * 4L > data.Length) throw new InvalidDataException($"{acdPath}: entry {name} runs past the end of the file");

            if (wanted(name))
            {
                var content = new byte[size];
                for (var i = 0; i < size; i++) content[i] = (byte)(data[position + i * 4] - key[i % key.Length]);
                decoded(name, content);
            }

            position += size * 4;
        }
    }

    /// <summary>The key string for a car folder name: eight bytes worked out from its characters, joined by dashes</summary>
    public static string KeyFor(string folderName)
    {
        var s = folderName.ToLowerInvariant().Select(c => (int)c).ToArray();
        var n = s.Length;

        var k1 = 0;
        foreach (var c in s) k1 += c;

        var k2 = 0;
        for (var i = 0; i < n - 1; i += 2) k2 = unchecked(k2 * s[i] - s[i + 1]);

        var k3 = 0;
        for (var i = 1; i < n - 3; i += 3) k3 = unchecked(k3 * s[i] / (s[i + 1] + 27) - 27 - s[i - 1]);

        var k4 = 0x1683;
        for (var i = 1; i < n; i++) k4 -= s[i];

        var k5 = 0x42;
        for (var i = 1; i < n - 4; i += 4) k5 = unchecked((s[i] + 15) * k5 * (s[i - 1] + 15) + 0x16);

        var k6 = 0x65;
        for (var i = 0; i < n - 2; i += 2) k6 -= s[i];

        var k7 = 0xAB;
        for (var i = 0; i < n - 2; i += 2) k7 %= s[i];

        var k8 = 0xAB;
        for (var i = 0; i < n - 1; i++) k8 = k8 / s[i] + s[i + 1];

        return string.Join("-", new[] { k1, k2, k3, k4, k5, k6, k7, k8 }.Select(k => (k & 0xFF).ToString(CultureInfo.InvariantCulture)));
    }
}
