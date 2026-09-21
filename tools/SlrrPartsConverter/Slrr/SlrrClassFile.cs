using System.IO;
using System.Text;

namespace Street_Rod_AC.Slrr;

public enum SlrrConstantKind
{
    String = 0,
    Resource = 3,
    Class = 4,
    Member = 5,
    NameAndType = 7
}

/// <summary>Constant pool entry: a string, or up to two indices/values depending on the kind</summary>
public readonly record struct SlrrConstant(SlrrConstantKind Kind, string? Text, int A, int B);

/// <summary>One node of a compiled method: opcode, source line and the operand some opcodes carry</summary>
public readonly record struct SlrrInstruction(byte Op, int Line, int Operand)
{
    public float FloatOperand => BitConverter.Int32BitsToSingle(Operand);
}

public sealed record SlrrField(int Flags, string Name, string Signature, int Tree)
{
    public bool IsStatic => (Flags & 8) != 0;
}

public sealed record SlrrMethod(int Flags, string Name, string Signature, int Tree);

/// <summary>
/// A compiled SLRR script ("TUFA" class file): sections CONS (constant pool), FILD (fields with the
/// index of their initializer), MTHD (methods with the index of their body) and TREE (the bodies,
/// postfix expression trees where every node carries its source line).
/// </summary>
public sealed class SlrrClassFile
{
    private const int HeaderSize = 12;

    // Opcodes followed by a 4-byte operand; derived from all 7589 class files of a modded install,
    // which parse to the last byte with exactly this table
    private static readonly HashSet<byte> WithOperand = new()
    {
        0x01, 0x02, 0x03, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0D, 0x0E, 0x11, 0x12, 0x14, 0x15, 0x16, 0x17,
        0x19, 0x1A, 0x1B, 0x1C, 0x25, 0x26, 0x27
    };

    private static readonly HashSet<byte> WithoutOperand = new()
    {
        0x04, 0x05, 0x0C, 0x10, 0x18, 0x20, 0x21, 0x22, 0x23, 0x24, 0x28, 0x29, 0x2A, 0x2B, 0x2C, 0x2D
    };

    public List<SlrrConstant> Pool { get; } = new();
    public List<SlrrField> Fields { get; } = new();
    public List<SlrrMethod> Methods { get; } = new();
    public List<SlrrInstruction[]> Trees { get; } = new();

    public string? ClassName => Text(0);
    public string? BaseClass => Pool.Count > 3 && Pool[3].Kind == SlrrConstantKind.Class ? Text(Pool[3].A) : Text(2);

    public string? Text(int index) => index >= 0 && index < Pool.Count ? Pool[index].Text : null;

    /// <summary>Name of the field or method behind a member constant</summary>
    public string? MemberName(int index)
    {
        if (index < 0 || index >= Pool.Count || Pool[index].Kind != SlrrConstantKind.Member) return null;

        var nameAndType = Pool[index].B;
        return nameAndType >= 0 && nameAndType < Pool.Count ? Text(Pool[nameAndType].A) : null;
    }

    /// <summary>Class a member constant belongs to</summary>
    public string? MemberClass(int index)
    {
        if (index < 0 || index >= Pool.Count || Pool[index].Kind != SlrrConstantKind.Member) return null;

        var owner = Pool[index].A;
        return owner >= 0 && owner < Pool.Count ? Text(Pool[owner].A) : null;
    }

    public static SlrrClassFile? Load(string filename)
    {
        var data = File.ReadAllBytes(filename);
        if (data.Length < HeaderSize + 8 || Encoding.ASCII.GetString(data, 0, 4) != "TUFA") return null;

        var result = new SlrrClassFile();
        try
        {
            var position = HeaderSize;
            while (position + 8 <= data.Length)
            {
                var tag = Encoding.ASCII.GetString(data, position, 4);
                var size = BitConverter.ToInt32(data, position + 4);
                var start = position + 8;
                if (size < 0 || (long)start + size > data.Length) break;

                switch (tag)
                {
                    case "CONS": result.ReadPool(data, start); break;
                    case "FILD": result.ReadFields(data, start); break;
                    case "MTHD": result.ReadMethods(data, start); break;
                    case "TREE": result.ReadTrees(data, start, start + size); break;
                }

                position = start + size;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidDataException)
        {
            return null;
        }

        return result.Pool.Count == 0 ? null : result;
    }

    private void ReadPool(byte[] data, int position)
    {
        var count = BitConverter.ToInt32(data, position);
        position += 4;

        for (var i = 0; i < count; i++)
        {
            var kind = (SlrrConstantKind)BitConverter.ToInt32(data, position);
            position += 4;

            switch (kind)
            {
                case SlrrConstantKind.String:
                    var length = BitConverter.ToInt32(data, position);
                    Pool.Add(new SlrrConstant(kind, Encoding.Latin1.GetString(data, position + 4, length), 0, 0));
                    position += 4 + length + 1;
                    break;
                case SlrrConstantKind.Class:
                    Pool.Add(new SlrrConstant(kind, null, BitConverter.ToInt32(data, position), 0));
                    position += 4;
                    break;
                case SlrrConstantKind.Resource:
                case SlrrConstantKind.Member:
                case SlrrConstantKind.NameAndType:
                    Pool.Add(new SlrrConstant(kind, null, BitConverter.ToInt32(data, position), BitConverter.ToInt32(data, position + 4)));
                    position += 8;
                    break;
                default:
                    throw new InvalidDataException($"Unknown constant kind {(int)kind}");
            }
        }
    }

    /// <summary>Static fields, then instance fields; each group is a count followed by (flags, name, signature, tree)</summary>
    private void ReadFields(byte[] data, int position)
    {
        for (var group = 0; group < 2; group++)
        {
            var count = BitConverter.ToInt32(data, position);
            position += 4;

            for (var i = 0; i < count; i++, position += 16)
            {
                Fields.Add(new SlrrField(
                    BitConverter.ToInt32(data, position),
                    Text(BitConverter.ToInt32(data, position + 4)) ?? string.Empty,
                    Text(BitConverter.ToInt32(data, position + 8)) ?? string.Empty,
                    BitConverter.ToInt32(data, position + 12)));
            }
        }
    }

    /// <summary>Static methods, then instance methods; each group is a count followed by (flags, name, signature, tree, locals)</summary>
    private void ReadMethods(byte[] data, int position)
    {
        for (var group = 0; group < 2; group++)
        {
            var count = BitConverter.ToInt32(data, position);
            position += 4;

            for (var i = 0; i < count; i++, position += 20)
            {
                Methods.Add(new SlrrMethod(
                    BitConverter.ToInt32(data, position),
                    Text(BitConverter.ToInt32(data, position + 4)) ?? string.Empty,
                    Text(BitConverter.ToInt32(data, position + 8)) ?? string.Empty,
                    BitConverter.ToInt32(data, position + 12)));
            }
        }
    }

    private void ReadTrees(byte[] data, int position, int end)
    {
        var count = BitConverter.ToInt32(data, position);
        position += 4;

        for (var i = 0; i < count; i++)
        {
            var length = BitConverter.ToInt32(data, position);
            position += 4;
            if (length < 0 || length > end - position) throw new InvalidDataException("Bad tree length");

            var tree = new SlrrInstruction[length];
            for (var j = 0; j < length; j++)
            {
                var op = data[position];
                var line = BitConverter.ToUInt16(data, position + 1);
                position += 3;

                var operand = 0;
                if (WithOperand.Contains(op))
                {
                    operand = BitConverter.ToInt32(data, position);
                    position += 4;
                }
                else if (!WithoutOperand.Contains(op))
                {
                    throw new InvalidDataException($"Unknown opcode 0x{op:X2}");
                }

                tree[j] = new SlrrInstruction(op, line, operand);
            }

            Trees.Add(tree);
        }
    }
}
