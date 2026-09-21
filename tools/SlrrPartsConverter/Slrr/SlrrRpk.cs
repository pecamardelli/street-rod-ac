using System.Globalization;
using System.IO;
using System.Text;

namespace Street_Rod_AC.Slrr;

/// <summary>
/// One record of an SLRR resource database. The payload is a small text script
/// ("mesh 0x..", "texture 0x..", "sourcefile path", "native part path.cfg").
/// </summary>
public sealed class SlrrRpkEntry
{
    public required int TypeId { get; init; }

    /// <summary>Parent type (category); the high word selects an external rpk like any other id</summary>
    public required int SuperId { get; init; }

    public required string Alias { get; init; }
    public required string[][] Lines { get; init; }

    /// <summary>Second token of the first line starting with the keyword, e.g. the path of "sourcefile"</summary>
    public string? FirstValue(string keyword) =>
        Lines.FirstOrDefault(l => l.Length > 1 && l[0].Equals(keyword, StringComparison.OrdinalIgnoreCase))?[1];

    public IEnumerable<int> Ids(string keyword) =>
        Lines.Where(l => l.Length > 1 && l[0].Equals(keyword, StringComparison.OrdinalIgnoreCase))
             .Select(l => SlrrRpk.ParseHex(l[1]));
}

/// <summary>
/// SLRR .rpk file: not an archive but a resource table that links parts, renders, meshes and textures by id.
/// Ids with a non-zero high word point into another rpk listed in the externals table.
/// </summary>
public sealed class SlrrRpk
{
    private const int ExternalRecordSize = 64;
    private const int EntryHeaderSize = 23;

    private readonly Dictionary<int, SlrrRpkEntry> _entries = new();

    /// <summary>Path relative to the game folder, as other rpks refer to it</summary>
    public string RelativePath { get; private init; } = string.Empty;

    private readonly Dictionary<int, string> _externals = new();

    public IReadOnlyDictionary<int, SlrrRpkEntry> Entries => _entries;

    /// <summary>External rpk paths (relative to the game folder) by reference index</summary>
    public IReadOnlyDictionary<int, string> Externals => _externals;

    public static SlrrRpk Load(string filename, string relativePath)
    {
        var data = File.ReadAllBytes(filename);
        if (data.Length < 16 || Encoding.ASCII.GetString(data, 0, 4) != "RPAK")
            throw new InvalidDataException($"Not an RPK file: {filename}");

        var rpk = new SlrrRpk { RelativePath = relativePath };
        var latin1 = Encoding.Latin1;

        var externalCount = BitConverter.ToInt32(data, 8);
        var position = 16;
        for (var i = 0; i < externalCount; i++, position += ExternalRecordSize)
        {
            var index = BitConverter.ToUInt16(data, position + 2);
            rpk._externals[index] = ReadZeroTerminated(data, position + 4, ExternalRecordSize - 4, latin1);
        }

        var entryCount = BitConverter.ToInt32(data, position + 4);
        position += 16;

        for (var i = 0; i < entryCount; i++)
        {
            // Some rpks declare more entries than they hold
            if (position + EntryHeaderSize > data.Length || position + EntryHeaderSize + data[position + 22] > data.Length) break;

            var superId = BitConverter.ToInt32(data, position);
            var typeId = BitConverter.ToInt32(data, position + 4);
            var offset = BitConverter.ToInt32(data, position + 14);
            var size = BitConverter.ToInt32(data, position + 18);
            var aliasLength = data[position + 22];
            var alias = ReadZeroTerminated(data, position + EntryHeaderSize, aliasLength, latin1);
            position += EntryHeaderSize + aliasLength;

            var text = size > 0 && offset >= 0 && (long)offset + size <= data.Length
                ? latin1.GetString(data, offset, size)
                : string.Empty;

            rpk._entries[typeId] = new SlrrRpkEntry
            {
                TypeId = typeId,
                SuperId = superId,
                Alias = alias,
                Lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                            .Select(l => l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                            .Where(l => l.Length > 0)
                            .ToArray()
            };
        }

        return rpk;
    }

    public static int ParseHex(string value)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value[2..];
        return int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result) ? result : -1;
    }

    private static string ReadZeroTerminated(byte[] data, int offset, int maxLength, Encoding encoding)
    {
        var length = 0;
        while (length < maxLength && data[offset + length] != 0) length++;
        return encoding.GetString(data, offset, length);
    }
}
