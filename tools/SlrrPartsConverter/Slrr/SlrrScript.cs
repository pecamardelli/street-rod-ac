using System.IO;
using System.Text;

namespace Street_Rod_AC.Slrr;

/// <summary>
/// The bits of a compiled SLRR part script ("TUFA" class file) that describe the part:
/// which game class it extends and what it is called in the catalog.
/// </summary>
public sealed class SlrrScript
{
    private const int ConstantString = 0;
    private const int ConstantClass = 4;
    private const int MinTextLength = 4;

    public string? ClassName { get; private init; }
    public string? BaseClass { get; private init; }
    public string? DisplayName { get; private init; }

    public static SlrrScript? Load(string filename)
    {
        var data = File.ReadAllBytes(filename);
        if (data.Length < 28 || Encoding.ASCII.GetString(data, 0, 4) != "TUFA" || Encoding.ASCII.GetString(data, 12, 4) != "CONS")
            return null;

        // The pool opens with: own class name, class ref, base class name, class ref
        var position = 24;
        var className = ReadStringConstant(data, ref position);
        SkipClassConstant(data, ref position);
        var baseClass = ReadStringConstant(data, ref position);

        var poolEnd = Math.Min(data.Length, 16 + BitConverter.ToInt32(data, 16));
        return new SlrrScript
        {
            ClassName = className,
            BaseClass = baseClass,
            DisplayName = FindDisplayName(data, position, poolEnd)
        };
    }

    private static string? ReadStringConstant(byte[] data, ref int position)
    {
        if (position + 8 > data.Length || BitConverter.ToInt32(data, position) != ConstantString) return null;

        var length = BitConverter.ToInt32(data, position + 4);
        if (length < 0 || position + 8 + length >= data.Length) return null;

        var value = Encoding.Latin1.GetString(data, position + 8, length);
        position += 8 + length + 1;
        return value;
    }

    private static void SkipClassConstant(byte[] data, ref int position)
    {
        if (position + 8 <= data.Length && BitConverter.ToInt32(data, position) == ConstantClass) position += 8;
    }

    /// <summary>
    /// The rest of the pool mixes constant kinds of unknown size, so instead of walking it this looks for
    /// the first piece of prose: identifiers, type signatures and paths never contain a space.
    /// </summary>
    private static string? FindDisplayName(byte[] data, int start, int end)
    {
        var text = new StringBuilder();
        for (var i = start; i <= end; i++)
        {
            var c = i < end ? data[i] : (byte)0;
            if (c >= 0x20 && c < 0x7F || c >= 0xA0)
            {
                text.Append((char)c);
                continue;
            }

            if (text.Length >= MinTextLength)
            {
                var candidate = text.ToString().Trim();
                if (candidate.Contains(' ') && !candidate.Contains('\\') && !candidate.StartsWith('(') && char.IsLetterOrDigit(candidate[0]))
                    return candidate;
            }
            text.Clear();
        }

        return null;
    }
}
