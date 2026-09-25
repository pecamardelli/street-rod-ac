using System.Diagnostics;
using System.Text;
using Street_Rod_AC.Parts.Scripting;

namespace StreetRodAC.Tests;

/// <summary>
/// The part-script VM fed bytecode no compiler would make: it must end cleanly, never throw, loop for ever or
/// run out of memory or stack.
/// </summary>
public sealed class ScriptVmTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static ScriptInstruction I(byte op, int operand = 0) => new(op, 1, operand);

    /// <summary>A class in memory with one instance method "m" whose body is the tree; pool[0] is the class name</summary>
    private static ScriptClass ClassWith(ScriptInstruction[] tree, params string[] extraTexts)
    {
        var type = new ScriptClass();
        type.Pool.Add(new ScriptConstant(ScriptConstantKind.String, "java.game.test.T", 0, 0));
        type.Pool.Add(new ScriptConstant(ScriptConstantKind.String, "m", 0, 0));
        type.Pool.Add(new ScriptConstant(ScriptConstantKind.String, "java.lang.Object", 0, 0));
        foreach (var text in extraTexts) type.Pool.Add(new ScriptConstant(ScriptConstantKind.String, text, 0, 0));
        type.Methods.Add(new ScriptMethod(0, "m", "()Ljava.lang.Object;", 0));
        type.Trees.Add(tree);
        return type;
    }

    private ScriptValue Run(ScriptClass type, out ScriptVm vm)
    {
        vm = new ScriptVm(new ScriptClassLoader(_temp.Path), new ScriptHost());
        var self = vm.Instantiate([type]);
        return vm.Call(self, "m");
    }

    [Fact]
    public void A_backward_jump_before_the_start_ends_the_tree()
    {
        var result = Run(ClassWith([I(0x0B, 1), I(0x14, -1_000_000)]), out var vm);
        Assert.IsType<ScriptUnknown>(result);
        Assert.False(vm.BudgetExhausted);
    }

    [Fact]
    public void A_false_branch_past_the_end_ends_the_tree()
    {
        var result = Run(ClassWith([I(0x0B, 0), I(0x15, int.MaxValue), I(0x0B, 5), I(0x10)]), out _);
        Assert.IsType<ScriptUnknown>(result);
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    public void Jumps_of_any_size_end_cleanly(int offset)
    {
        var tree = new[] { I(0x0B, 1), I(0x14, offset), I(0x0B, 7), I(0x10) };
        var result = Run(ClassWith(tree), out _);
        Assert.NotNull(result);
    }

    [Fact]
    public void An_unknown_condition_with_a_jump_past_the_end_ends_cleanly()
    {
        var tree = new[] { I(0x0D), I(0x15, int.MaxValue), I(0x0B, 5), I(0x28), I(0x14, int.MinValue), I(0x2A) };
        Assert.NotNull(Run(ClassWith(tree), out _));
    }

    [Fact]
    public void The_short_circuit_jump_out_of_range_ends_cleanly()
    {
        var tree = new[] { I(0x0B, 0), I(0x04), I(0x14, -50), I(0x0B, 1), I(0x07, 2), I(0x10) };
        Assert.NotNull(Run(ClassWith(tree), out _));
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(int.MinValue)]
    [InlineData(1000)]
    public void An_argument_count_out_of_range_is_clamped(int count)
    {
        // push 1, argcount, call "m"-named text 1 on self: the VM takes what the stack holds, never more or less than none
        var tree = new[] { I(0x0B, 1), I(0x27, count), I(0x12, 1), I(0x10) };
        var result = Run(ClassWith(tree), out _);
        Assert.NotNull(result);
    }

    /// <summary>s = "ab"; then s = s + s, n times; return s. Local 1 (local 0 is this).</summary>
    private static ScriptInstruction[] Doubling(int times)
    {
        var tree = new List<ScriptInstruction> { I(0x09, 3), I(0x01, 1), I(0x08, 35) };
        for (var i = 0; i < times; i++)
            tree.AddRange([I(0x01, 1), I(0x01, 1), I(0x07, 16), I(0x01, 1), I(0x08, 35)]);
        tree.AddRange([I(0x01, 1), I(0x10)]);
        return tree.ToArray();
    }

    [Fact]
    public void Text_built_by_adding_strings_works_below_the_cap()
    {
        var result = Run(ClassWith(Doubling(10), "ab"), out _);
        Assert.Equal(2048, Assert.IsType<ScriptText>(result).Content.Length);
    }

    [Fact]
    public void Text_doubled_past_the_cap_becomes_unknown_instead_of_eating_memory()
    {
        var result = Run(ClassWith(Doubling(40), "ab"), out _);
        Assert.IsType<ScriptUnknown>(result);
    }

    [Fact]
    public void An_endless_doubling_loop_stops_on_the_step_budget()
    {
        // s = "ab"; loop: s = s + s; jump back to loop
        var tree = new[] { I(0x09, 3), I(0x01, 1), I(0x08, 35), I(0x01, 1), I(0x01, 1), I(0x07, 16), I(0x01, 1), I(0x08, 35), I(0x14, -5) };
        var clock = Stopwatch.StartNew();
        Run(ClassWith(tree, "ab"), out var vm);
        Assert.True(vm.BudgetExhausted);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"took {clock.Elapsed}");
    }

    [Fact]
    public void A_printf_precision_of_a_billion_digits_is_clamped()
    {
        var host = new ScriptHost();
        var vm = new ScriptVm(new ScriptClassLoader(_temp.Path), host);
        var text = host.CallStatic(vm, "java.lang.Float", "toString", [ScriptValue.Of(1.5), ScriptValue.Of("%.999999999f hp")]);
        Assert.StartsWith("1.5000", Assert.IsType<ScriptText>(text).Content);
        Assert.EndsWith(" hp", ((ScriptText)text).Content);
    }

    // ---- Static initializer chains, through class files on disk ----

    [Theory]
    [InlineData(5, true)]
    [InlineData(200, false)]
    public void A_chain_of_static_initializers_is_cut_at_the_depth_limit(int length, bool reachesTheEnd)
    {
        var folder = _temp.Combine("chain");
        Directory.CreateDirectory(folder);
        for (var i = 0; i < length; i++) TufaWriter.WriteStaticLink(folder, i, last: i == length - 1);

        var loader = new ScriptClassLoader(_temp.Path);
        var chain = loader.Chain("java.game.chain.C0", folder);
        Assert.NotNull(chain);

        var vm = new ScriptVm(loader, new ScriptHost());
        var instance = vm.Instantiate(chain!);

        var value = instance.Fields["v"];
        if (reachesTheEnd) Assert.Equal(7, Assert.IsType<ScriptNumber>(value).Amount);
        else Assert.IsType<ScriptUnknown>(value);
    }

    [Fact]
    public void A_class_that_extends_itself_is_one_class_not_an_endless_chain()
    {
        var folder = _temp.Combine("self");
        Directory.CreateDirectory(folder);
        TufaWriter.Write(Path.Combine(folder, "S.class"), ["java.game.self.S", "x", "java.game.self.S"], [], [], []);

        var chain = new ScriptClassLoader(_temp.Path).Chain("java.game.self.S", folder);
        Assert.Single(chain!);
    }

    [Fact]
    public void A_class_file_cut_short_or_with_nonsense_is_no_class()
    {
        var file = TufaWriter.Write(_temp.Combine("x", "Broken.class"), ["java.game.x.Broken", "m", "java.lang.Object"],
            [], [(0, 1, 2, 0)], [[I(0x0B, 1), I(0x10)]]);
        var bytes = File.ReadAllBytes(file);
        Assert.NotNull(ScriptClass.Load(file));

        for (var cut = 20; cut < bytes.Length; cut += 3)
        {
            File.WriteAllBytes(file, bytes[..cut]);
            ScriptClass.Load(file); // null or a partial class, never an exception
        }

        var nonsense = (byte[])bytes.Clone();
        for (var i = 12; i < nonsense.Length; i += 5) nonsense[i] = 0xFF;
        File.WriteAllBytes(file, nonsense);
        ScriptClass.Load(file);
    }

    // ---- Arrays and literals ----

    /// <summary>float[] a = new float[3]; a[index] = 9; return a. Local 1; pool[3] is the array type</summary>
    private static ScriptClass StoringAt(int index) => ClassWith(
        [I(0x0B, 3), I(0x07, 31), I(0x06, 3), I(0x01, 1), I(0x08, 35),
         I(0x0B, 9), I(0x0B, index), I(0x01, 1), I(0x20), I(0x08, 35),
         I(0x01, 1), I(0x10)], "[F");

    [Fact]
    public void A_store_inside_the_array_is_made()
    {
        var array = Assert.IsType<ScriptArray>(Run(StoringAt(2), out _));
        Assert.Equal(3, array.Length);
        Assert.Equal(9, Assert.IsType<ScriptNumber>(array.Get(2)).Amount);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-5)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void A_store_outside_the_array_is_not_made(int index)
    {
        var array = Assert.IsType<ScriptArray>(Run(StoringAt(index), out _));
        Assert.Empty(array.Items);
    }

    [Theory]
    [InlineData(2.66f, 2.66)]
    [InlineData(0.1f, 0.1)]
    [InlineData(-1234.5f, -1234.5)]
    [InlineData(3.4028235E+38f, 3.4028235E+38)]
    public void A_float_literal_reads_as_the_number_written_not_its_float_approximation(float literal, double expected)
    {
        var result = Run(ClassWith([I(0x0A, BitConverter.SingleToInt32Bits(literal)), I(0x10)]), out _);
        var number = Assert.IsType<ScriptNumber>(result);
        Assert.Equal(expected, number.Amount);
        Assert.False(number.IsInteger);
    }
}


/// <summary>Writes compiled-script class files ("TUFA") the way <see cref="ScriptClass.Load"/> reads them</summary>
internal static class TufaWriter
{
    private static readonly HashSet<byte> WithOperand =
        [0x01, 0x02, 0x03, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0D, 0x0E, 0x11, 0x12, 0x14, 0x15, 0x16, 0x17, 0x19, 0x1A, 0x1B, 0x1C, 0x25, 0x26, 0x27];

    /// <summary>A pool entry: a string, or (kind, a, b) for class/member/name-and-type constants</summary>
    public abstract record Constant;
    public sealed record Text(string Value) : Constant;
    public sealed record Ref(ScriptConstantKind Kind, int A, int B = 0) : Constant;

    public static string Write(string path, string[] texts, (int Flags, int Name, int Signature, int Tree)[] fields,
        (int Flags, int Name, int Signature, int Tree)[] methods, ScriptInstruction[][] trees) =>
        Write(path, texts.Select(t => (Constant)new Text(t)).ToArray(), fields, methods, trees);

    public static string Write(string path, Constant[] pool, (int Flags, int Name, int Signature, int Tree)[] fields,
        (int Flags, int Name, int Signature, int Tree)[] methods, ScriptInstruction[][] trees)
    {
        using var data = new MemoryStream();
        var w = new BinaryWriter(data);
        w.Write(Encoding.ASCII.GetBytes("TUFA"));
        w.Write(0);
        w.Write(0);

        Section(w, "CONS", s =>
        {
            s.Write(pool.Length);
            foreach (var constant in pool)
            {
                switch (constant)
                {
                    case Text text:
                        s.Write((int)ScriptConstantKind.String);
                        var bytes = Encoding.Latin1.GetBytes(text.Value);
                        s.Write(bytes.Length);
                        s.Write(bytes);
                        s.Write((byte)0);
                        break;
                    case Ref { Kind: ScriptConstantKind.Class } r:
                        s.Write((int)r.Kind);
                        s.Write(r.A);
                        break;
                    case Ref r:
                        s.Write((int)r.Kind);
                        s.Write(r.A);
                        s.Write(r.B);
                        break;
                }
            }
        });

        // Statics first, then instance members: a static has flag 8
        Section(w, "FILD", s =>
        {
            foreach (var group in new[] { fields.Where(f => (f.Flags & 8) != 0), fields.Where(f => (f.Flags & 8) == 0) })
            {
                var list = group.ToList();
                s.Write(list.Count);
                foreach (var f in list) { s.Write(f.Flags); s.Write(f.Name); s.Write(f.Signature); s.Write(f.Tree); }
            }
        });

        Section(w, "MTHD", s =>
        {
            foreach (var group in new[] { methods.Where(m => (m.Flags & 8) != 0), methods.Where(m => (m.Flags & 8) == 0) })
            {
                var list = group.ToList();
                s.Write(list.Count);
                foreach (var m in list) { s.Write(m.Flags); s.Write(m.Name); s.Write(m.Signature); s.Write(m.Tree); s.Write(0); }
            }
        });

        Section(w, "TREE", s =>
        {
            s.Write(trees.Length);
            foreach (var tree in trees)
            {
                s.Write(tree.Length);
                foreach (var instruction in tree)
                {
                    s.Write(instruction.Op);
                    s.Write((ushort)instruction.Line);
                    if (WithOperand.Contains(instruction.Op)) s.Write(instruction.Operand);
                }
            }
        });

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    private static void Section(BinaryWriter w, string tag, Action<BinaryWriter> body)
    {
        using var content = new MemoryStream();
        body(new BinaryWriter(content));
        w.Write(Encoding.ASCII.GetBytes(tag));
        w.Write((int)content.Length);
        w.Write(content.ToArray());
    }

    /// <summary>
    /// Class Ci of package java.game.chain: static int v = C(i+1).v; the last one: static int v = 7.
    /// Pool: 0 own name, 1 "v", 2 base, 3 "I", 4 next's name, 5 Class(4), 6 NameAndType(1, 3), 7 Member(5, 6)
    /// </summary>
    public static void WriteStaticLink(string folder, int i, bool last)
    {
        var pool = new Constant[]
        {
            new Text($"java.game.chain.C{i}"), new Text("v"), new Text("java.lang.Object"), new Text("I"),
            new Text($"java.game.chain.C{i + 1}"), new Ref(ScriptConstantKind.Class, 4),
            new Ref(ScriptConstantKind.NameAndType, 1, 3), new Ref(ScriptConstantKind.Member, 5, 6)
        };
        var tree = last
            ? new[] { new ScriptInstruction(0x0B, 1, 7), new ScriptInstruction(0x10, 1, 0) }
            : new[] { new ScriptInstruction(0x1B, 1, 7), new ScriptInstruction(0x10, 1, 0) };
        Write(Path.Combine(folder, $"C{i}.class"), pool, [(8, 1, 3, 0)], [], [tree]);
    }
}
