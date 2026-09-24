using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Street_Rod_AC.Parts.Scripting;

public enum ScriptConstantKind
{
    String = 0,
    Resource = 3,
    Class = 4,
    Member = 5,
    NameAndType = 7
}

/// <summary>Constant pool entry: a string, or up to two indices/values depending on the kind</summary>
public readonly record struct ScriptConstant(ScriptConstantKind Kind, string? Text, int A, int B);

/// <summary>One node of a compiled method: opcode, source line and the operand some opcodes carry</summary>
public readonly record struct ScriptInstruction(byte Op, int Line, int Operand)
{
    public float FloatOperand => BitConverter.Int32BitsToSingle(Operand);
}

public sealed record ScriptField(int Flags, string Name, string Signature, int Tree)
{
    public bool IsStatic => (Flags & 8) != 0;
}

public sealed record ScriptMethod(int Flags, string Name, string Signature, int Tree)
{
    public bool IsStatic => (Flags & 8) != 0;
    public bool IsNative => (Flags & 0x40) != 0;

    /// <summary>Number of parameters in a signature like "(ILjava.lang.String;[F)V"</summary>
    public int ParameterCount => ParameterTypes.Count;

    /// <summary>Signature of every parameter, first one first: "I", "Ljava.lang.String;", "[F"</summary>
    public IReadOnlyList<string> ParameterTypes { get; } = ParseParameters(Signature);

    /// <summary>What follows the parameters: "V", "F", "Ljava.lang.String;"</summary>
    public string ReturnType => Signature[(Signature.LastIndexOf(')') + 1)..];

    private static List<string> ParseParameters(string signature)
    {
        var types = new List<string>();
        for (var i = signature.IndexOf('(') + 1; i > 0 && i < signature.Length && signature[i] != ')'; i++)
        {
            var start = i;
            while (i < signature.Length && signature[i] == '[') i++;
            if (i < signature.Length && signature[i] == 'L') i = signature.IndexOf(';', i);
            if (i < 0) break;
            types.Add(signature[start..Math.Min(i + 1, signature.Length)]);
        }

        return types;
    }
}

/// <summary>
/// A compiled SLRR script ("TUFA" class file): sections CONS (constant pool), FILD (fields with the
/// index of their initializer), MTHD (methods with the index of their body) and TREE (the bodies,
/// postfix expression trees where every node carries its source line).
/// </summary>
public sealed class ScriptClass
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

    public List<ScriptConstant> Pool { get; } = new();
    public List<ScriptField> Fields { get; } = new();
    public List<ScriptMethod> Methods { get; } = new();
    public List<ScriptInstruction[]> Trees { get; } = new();

    /// <summary>Folder the class was loaded from; classes of one car or pack refer to each other from there</summary>
    public string Folder { get; private set; } = string.Empty;

    /// <summary>
    /// The class this one is a copy of (<see cref="DerivedAs"/>): an object of it is also an instance of that,
    /// as far as the scripts that test for it can tell (the OHV cylinder head asks its block for the camshaft
    /// only when the block is a Block_Vee_OHV)
    /// </summary>
    public string? CopiedFrom { get; private set; }

    public string? ClassName => Text(0);
    public string? BaseClass => Pool.Count > 3 && Pool[3].Kind == ScriptConstantKind.Class ? Text(Pool[3].A) : Text(2);

    public string? Text(int index) => index >= 0 && index < Pool.Count ? Pool[index].Text : null;

    private Dictionary<string, string>? _fieldSignatures;

    /// <summary>Declared type of a field of this class; null when the class has no such field</summary>
    public string? FieldSignature(string name)
    {
        if (_fieldSignatures == null)
        {
            var signatures = new Dictionary<string, string>();
            foreach (var field in Fields) signatures.TryAdd(field.Name, field.Signature);
            _fieldSignatures = signatures;
        }

        return _fieldSignatures.GetValueOrDefault(name);
    }

    // Method lookups of the VM, made once: a class is complete when it is loaded (or derived) and never changes
    // after, and every virtual call and construction asks. Built on first use, from any thread.
    private volatile MethodIndex? _methodIndex;

    private sealed class MethodIndex
    {
        public readonly Dictionary<(string Name, int ParameterCount), ScriptMethod> ByNameAndCount = new();
        public readonly Dictionary<string, List<ScriptMethod>> ByName = new(StringComparer.Ordinal);
    }

    private MethodIndex GetMethodIndex()
    {
        if (_methodIndex is { } built) return built;

        var index = new MethodIndex();
        foreach (var method in Methods)
        {
            // The first one declared wins, as a search of the list in order would find it
            index.ByNameAndCount.TryAdd((method.Name, method.ParameterCount), method);
            if (!index.ByName.TryGetValue(method.Name, out var named)) index.ByName[method.Name] = named = new List<ScriptMethod>();
            named.Add(method);
        }

        return _methodIndex = index;
    }

    /// <summary>The first method of this class with the name and number of parameters; null when there is none</summary>
    public ScriptMethod? FindMethod(string name, int parameterCount) =>
        GetMethodIndex().ByNameAndCount.GetValueOrDefault((name, parameterCount));

    /// <summary>Methods of this class by name ("&lt;init&gt;": the constructors), in the order they are declared</summary>
    public IReadOnlyList<ScriptMethod> MethodsNamed(string name) =>
        GetMethodIndex().ByName.TryGetValue(name, out var named) ? named : Array.Empty<ScriptMethod>();

    /// <summary>Class name behind a type operand, which is a signature string: "Ljava.game.parts.Part;"</summary>
    public string? TypeName(int index)
    {
        var text = Text(index);
        return text is { Length: > 2 } && text[0] == 'L' && text[^1] == ';' ? text[1..^1] : text;
    }

    /// <summary>Class name behind a class constant</summary>
    public string? ClassAt(int index) =>
        index >= 0 && index < Pool.Count && Pool[index].Kind == ScriptConstantKind.Class ? Text(Pool[index].A) : null;

    /// <summary>Name of the field or method behind a member constant</summary>
    public string? MemberName(int index)
    {
        if (index < 0 || index >= Pool.Count || Pool[index].Kind != ScriptConstantKind.Member) return null;

        var nameAndType = Pool[index].B;
        return nameAndType >= 0 && nameAndType < Pool.Count ? Text(Pool[nameAndType].A) : null;
    }

    /// <summary>Class a member constant belongs to</summary>
    public string? MemberClass(int index)
    {
        if (index < 0 || index >= Pool.Count || Pool[index].Kind != ScriptConstantKind.Member) return null;

        var owner = Pool[index].A;
        return owner >= 0 && owner < Pool.Count ? Text(Pool[owner].A) : null;
    }

    /// <summary>
    /// The same code under another name: a class a mod extends that the game never shipped, which its author
    /// wrote by copying one it did (an inline OHV block is the vee one less the second cylinder head). Every
    /// mention of the copied class and of its base is swapped, and the methods that only made sense for the
    /// original are left out. The trees are shared: nothing in them names a class.
    /// </summary>
    public ScriptClass DerivedAs(string className, string baseClass, IEnumerable<string> droppedMethods)
    {
        var oldName = Regex.Escape(ClassName ?? string.Empty) + @"(?!\w)";
        var oldBase = Regex.Escape(BaseClass ?? string.Empty) + @"(?!\w)";
        var dropped = droppedMethods.ToHashSet(StringComparer.Ordinal);

        var derived = new ScriptClass { Folder = Folder, CopiedFrom = ClassName };
        derived.Pool.AddRange(Pool.Select(c => c.Text == null ? c : c with { Text = Swap(c.Text) }));
        derived.Fields.AddRange(Fields.Select(f => f with { Signature = Swap(f.Signature) }));
        // A new method, not a copy "with" another signature: the parameter types are worked out from the
        // signature when a method is made, and a copy would keep the old class names in them
        derived.Methods.AddRange(Methods.Where(m => !dropped.Contains(m.Name)).Select(m => new ScriptMethod(m.Flags, m.Name, Swap(m.Signature), m.Tree)));
        derived.Trees.AddRange(Trees);
        return derived;

        string Swap(string text) => Regex.Replace(Regex.Replace(text, oldName, className), oldBase, baseClass);
    }

    /// <summary>The class in a file; null when the file cannot be read or is not a class file</summary>
    public static ScriptClass? Load(string filename)
    {
        byte[] data;
        try
        {
            data = File.ReadAllBytes(filename);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Locked, unreadable or not a usable path: to the scripts, a class that is not there
            return null;
        }

        if (data.Length < HeaderSize + 8 || Encoding.ASCII.GetString(data, 0, 4) != "TUFA") return null;

        var result = new ScriptClass { Folder = Path.GetDirectoryName(filename) ?? string.Empty };
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
            var kind = (ScriptConstantKind)BitConverter.ToInt32(data, position);
            position += 4;

            switch (kind)
            {
                case ScriptConstantKind.String:
                    var length = BitConverter.ToInt32(data, position);
                    Pool.Add(new ScriptConstant(kind, Encoding.Latin1.GetString(data, position + 4, length), 0, 0));
                    position += 4 + length + 1;
                    break;
                case ScriptConstantKind.Class:
                    Pool.Add(new ScriptConstant(kind, null, BitConverter.ToInt32(data, position), 0));
                    position += 4;
                    break;
                case ScriptConstantKind.Resource:
                case ScriptConstantKind.Member:
                case ScriptConstantKind.NameAndType:
                    Pool.Add(new ScriptConstant(kind, null, BitConverter.ToInt32(data, position), BitConverter.ToInt32(data, position + 4)));
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
                Fields.Add(new ScriptField(
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
                Methods.Add(new ScriptMethod(
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

            var tree = new ScriptInstruction[length];
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

                tree[j] = new ScriptInstruction(op, line, operand);
            }

            Trees.Add(tree);
        }
    }
}
