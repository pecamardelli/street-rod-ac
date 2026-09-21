using System.Globalization;
using System.IO;

namespace Street_Rod_AC.Slrr;

/// <summary>What a part script says about its part once its constructor chain has run</summary>
public sealed class SlrrPartScript
{
    public string? ClassName { get; init; }

    /// <summary>Base classes from the nearest up to Part</summary>
    public List<string> ClassChain { get; init; } = new();

    /// <summary>Field values after construction: numbers, strings, "rpk#0xID" resources, or lists of those</summary>
    public Dictionary<string, object> Properties { get; init; } = new();

    /// <summary>Values that updatevariables() changes on a freshly built, uninstalled part</summary>
    public Dictionary<string, object> Derived { get; init; } = new();

    public List<SlrrStockPart> StockParts { get; init; } = new();
    public List<SlrrSlotRule> RequiredSlots { get; init; } = new();

    public string? BaseClass => ClassChain.FirstOrDefault();
    public string? DisplayName => Properties.TryGetValue("name", out var name) ? name as string : null;
}

/// <summary>A part that addStockParts() mounts; conditional when it depends on the car or on random wear</summary>
public sealed record SlrrStockPart(string Rpk, int TypeId, string? Name, bool Conditional);

/// <summary>"The part does not run without something on this slot", with the game's own explanation</summary>
public sealed record SlrrSlotRule(int Slot, string Message);

/// <summary>
/// Runs compiled part scripts far enough to know the parts: an interpreter for the postfix trees of
/// <see cref="SlrrClassFile"/> that executes field initializers and constructors along the class chain,
/// then addStockParts(), isDynoable()/isDriveable() and updatevariables(). Everything that depends on
/// the running game (natives, other objects) evaluates to "unknown"; branches on unknown conditions
/// are walked without taking effect, except for what they reveal (stock parts, slot rules).
/// </summary>
public sealed class SlrrScriptEvaluator
{
    private const string ClassPrefix = "java.game.";
    private const string ScriptsFolder = "scripts";

    private readonly SlrrGame _game;
    private readonly Dictionary<string, SlrrClassFile?> _classes = new(StringComparer.OrdinalIgnoreCase);

    public SlrrScriptEvaluator(SlrrGame game)
    {
        _game = game;
    }

    /// <summary>Static fields of every class evaluated so far, by class name</summary>
    public SortedDictionary<string, Dictionary<string, object>> Constants { get; } = new(StringComparer.Ordinal);

    public SlrrPartScript? Evaluate(string scriptFile)
    {
        var script = SlrrClassFile.Load(scriptFile);
        if (script == null) return null;

        var chain = new List<SlrrClassFile> { script };
        for (var current = script; chain.Count < 32;)
        {
            var parent = current.BaseClass == null ? null : FindClass(current.BaseClass);
            if (parent == null || chain.Contains(parent)) break;

            chain.Add(parent);
            current = parent;
        }

        var machine = new Machine(chain);
        machine.Construct();
        var properties = machine.Snapshot();
        foreach (var (type, constants) in machine.Constants()) Constants.TryAdd(type, constants);

        machine.Call("addStockParts", 1);
        machine.CollectRules("isDynoable");
        machine.CollectRules("isDriveable");

        // Branches for different cars often mount the same part
        var stockParts = machine.StockParts
            .GroupBy(p => (p.Rpk.ToLowerInvariant(), p.TypeId))
            .Select(g => g.First() with { Conditional = g.All(p => p.Conditional) })
            .ToList();
        var rules = machine.Rules.GroupBy(r => r.Slot).Select(g => g.First()).ToList();

        machine.Call("updatevariables", 0);
        var derived = machine.Snapshot()
            .Where(p => !properties.TryGetValue(p.Key, out var before) || !SameValue(before, p.Value))
            .ToDictionary(p => p.Key, p => p.Value);

        return new SlrrPartScript
        {
            ClassName = script.ClassName,
            ClassChain = chain.Skip(1).Select(c => c.ClassName ?? string.Empty).ToList(),
            Properties = properties,
            Derived = derived,
            StockParts = stockParts,
            RequiredSlots = rules
        };
    }

    private static bool SameValue(object a, object b)
    {
        if (a is List<object> listA && b is List<object> listB) return listA.SequenceEqual(listB);
        return a.Equals(b);
    }

    /// <summary>
    /// java.game.parts.engines.Mopar.X lives in parts\engines\Mopar\scripts\X.class, but the shared
    /// classes of java.game.parts.enginepart.block live in parts\scripts\enginepart\block:
    /// the scripts folder may sit at any level of the package path.
    /// </summary>
    private SlrrClassFile? FindClass(string className)
    {
        if (_classes.TryGetValue(className, out var cached)) return cached;

        SlrrClassFile? result = null;
        if (className.StartsWith(ClassPrefix, StringComparison.Ordinal))
        {
            var segments = className[ClassPrefix.Length..].Split('.');
            var package = segments[..^1];
            var file = segments[^1] + ".class";

            for (var level = package.Length; level >= 0 && result == null; level--)
            {
                var path = Path.Combine(new[] { _game.Root }
                    .Concat(package[..level]).Append(ScriptsFolder).Concat(package[level..]).Append(file).ToArray());
                if (File.Exists(path)) result = SlrrClassFile.Load(path);
            }
        }

        return _classes[className] = result;
    }

    #region Values

    private abstract record Value;
    private sealed record Number(double Amount, bool IsInteger) : Value;
    private sealed record Text(string Content) : Value;
    private sealed record Resource(string Rpk, int Id) : Value;
    private sealed record Null : Value;
    /// <summary>Not knowable offline; remembers the slot it was read from, so checks on it can be attributed</summary>
    private sealed record Unknown(int? Slot = null) : Value;
    private sealed record FieldRef(string Name) : Value;
    private sealed record LocalRef(int Index) : Value;
    private sealed record ElementRef(Value Array, Value Index) : Value;
    private sealed record Marker(int Length) : Value;
    private sealed record ArgumentCount(int Count) : Value;
    private sealed record NewObject : Value;

    private sealed record ArrayValue(int Length) : Value
    {
        public SortedDictionary<int, Value> Items { get; } = new();
    }

    private static readonly Value UnknownValue = new Unknown();
    private static readonly Value NullValue = new Null();

    #endregion

    private sealed class Machine
    {
        private const int MaxDepth = 12;
        private const int MaxSteps = 200_000;
        private const int StaticFlag = 8;
        private const string MaxWearProperty = "max_wear";
        private const string TuningBackupPrefix = "old_";

        private readonly List<SlrrClassFile> _chain;
        private readonly HashSet<string> _chainNames;
        private readonly Dictionary<string, Value> _fields = new();
        private readonly HashSet<string> _hidden = new();
        private int _steps;

        public Machine(List<SlrrClassFile> chain)
        {
            _chain = chain;
            _chainNames = chain.Select(c => c.ClassName ?? string.Empty).ToHashSet();
        }

        public List<SlrrStockPart> StockParts { get; } = new();
        public List<SlrrSlotRule> Rules { get; } = new();

        /// <summary>Field initializers and constructor of every class, base classes first</summary>
        public void Construct()
        {
            for (var i = _chain.Count - 1; i >= 0; i--)
            {
                var type = _chain[i];
                foreach (var field in type.Fields)
                {
                    // Constants and the backups tuning menus keep say nothing about the part
                    if (field.IsStatic || field.Name.StartsWith(TuningBackupPrefix, StringComparison.Ordinal)) _hidden.Add(field.Name);

                    if (field.Tree < 0 || field.Tree >= type.Trees.Count)
                    {
                        _fields[field.Name] = DefaultValue(field.Signature);
                        continue;
                    }

                    var value = Run(i, type.Trees[field.Tree], Array.Empty<Value>(), false, false, 0);
                    if (value != null) _fields[field.Name] = value;
                }

                var constructor = type.Methods.FirstOrDefault(m => m.Name == "<init>" && m.Signature == "(I)")
                                  ?? type.Methods.FirstOrDefault(m => m.Name == "<init>");
                if (constructor != null) Invoke(i, constructor, new[] { UnknownValue }, false, 0);
            }
        }

        private static Value DefaultValue(string signature) => signature switch
        {
            "F" or "D" => new Number(0, false),
            "I" or "Z" or "B" or "S" or "C" or "J" => new Number(0, true),
            _ => NullValue
        };

        public void Call(string method, int argumentCount)
        {
            _steps = 0;
            InvokeVirtual(0, method, Enumerable.Repeat(UnknownValue, argumentCount).ToArray(), false, 0);
        }

        /// <summary>Runs a check method only for the "missing part" answers it can give; state changes are dropped</summary>
        public void CollectRules(string method)
        {
            _steps = 0;
            var backup = new Dictionary<string, Value>(_fields);
            var stockParts = StockParts.Count;

            InvokeVirtual(0, method, Array.Empty<Value>(), false, 0);

            _fields.Clear();
            foreach (var (name, value) in backup) _fields[name] = value;
            StockParts.RemoveRange(stockParts, StockParts.Count - stockParts);
        }

        /// <summary>Static fields per declaring class: the constants the shared part logic runs on</summary>
        public Dictionary<string, Dictionary<string, object>> Constants()
        {
            var result = new Dictionary<string, Dictionary<string, object>>();
            foreach (var type in _chain)
            {
                var constants = new Dictionary<string, object>();
                foreach (var field in type.Fields.Where(f => f.IsStatic))
                {
                    if (_fields.TryGetValue(field.Name, out var value) && Export(value) is { } exported) constants[field.Name] = exported;
                }

                if (constants.Count > 0 && type.ClassName != null) result[type.ClassName] = constants;
            }

            return result;
        }

        public Dictionary<string, object> Snapshot()
        {
            var result = new Dictionary<string, object>();
            foreach (var (name, value) in _fields)
            {
                if (_hidden.Contains(name)) continue;

                var exported = Export(value);
                if (exported != null) result[name] = exported;
            }

            return result;
        }

        private static object? Export(Value value)
        {
            switch (value)
            {
                case Number number:
                    return number.IsInteger ? (long)number.Amount : (object)Math.Round(number.Amount, 6);
                case Text text:
                    return text.Content;
                case Resource resource:
                    return $"{resource.Rpk}#0x{resource.Id:X4}";
                case ArrayValue array:
                    var length = Math.Max(array.Length, array.Items.Count == 0 ? 0 : array.Items.Keys.Max() + 1);
                    if (length == 0 || array.Items.Count == 0) return null;

                    var items = new List<object>();
                    for (var i = 0; i < length; i++)
                        items.Add(array.Items.TryGetValue(i, out var item) ? Export(item) ?? 0L : 0L);
                    return items;
                default:
                    return null;
            }
        }

        #region Calls

        private Value InvokeVirtual(int fromClass, string name, Value[] arguments, bool uncertain, int depth)
        {
            for (var i = fromClass; i < _chain.Count; i++)
            {
                var method = _chain[i].Methods.FirstOrDefault(m => m.Name == name && ParameterCount(m.Signature) == arguments.Length);
                if (method != null) return Invoke(i, method, arguments, uncertain, depth);
            }

            return UnknownValue;
        }

        private Value Invoke(int classIndex, SlrrMethod method, Value[] arguments, bool uncertain, int depth)
        {
            var type = _chain[classIndex];
            if (depth > MaxDepth || method.Tree < 0 || method.Tree >= type.Trees.Count) return UnknownValue;

            var isStatic = (method.Flags & StaticFlag) != 0;
            return Run(classIndex, type.Trees[method.Tree], arguments, !isStatic, uncertain, depth + 1) ?? UnknownValue;
        }

        private Value CallSelf(string name, Value[] arguments, bool uncertain, int depth)
        {
            switch (name)
            {
                case "setMaxWear" when arguments.Length == 1:
                    if (!uncertain && arguments[0] is Number) _fields[MaxWearProperty] = arguments[0];
                    return UnknownValue;

                case "partOnSlot" when arguments.Length == 1:
                    return arguments[0] is Number { IsInteger: true } slot ? new Unknown((int)slot.Amount) : UnknownValue;

                case "addPart" when arguments.Length >= 2:
                    if (arguments[0] is Resource resource)
                        StockParts.Add(new SlrrStockPart(resource.Rpk, resource.Id, (arguments[1] as Text)?.Content, uncertain));
                    return UnknownValue;
            }

            return InvokeVirtual(0, name, arguments, uncertain, depth);
        }

        private static int ParameterCount(string signature)
        {
            var count = 0;
            for (var i = signature.IndexOf('(') + 1; i > 0 && i < signature.Length && signature[i] != ')'; i++)
            {
                while (i < signature.Length && signature[i] == '[') i++;
                if (i < signature.Length && signature[i] == 'L') i = signature.IndexOf(';', i);
                if (i < 0) break;
                count++;
            }

            return count;
        }

        #endregion

        /// <summary>
        /// Executes one tree. <paramref name="uncertain"/> means the caller does not know whether this code
        /// really runs, so nothing may change state. Returns the value of the first return (or initializer end).
        /// </summary>
        private Value? Run(int classIndex, SlrrInstruction[] tree, Value[] arguments, bool hasThis, bool uncertain, int depth)
        {
            var type = _chain[classIndex];
            var stack = new List<Value>();
            var locals = new Dictionary<int, Value>();

            // Parameters are numbered from the last one back; local 0 is "this" when there is one
            for (var i = 0; i < arguments.Length; i++) locals[arguments.Length - 1 - i + (hasThis ? 1 : 0)] = arguments[i];

            var declaredLocal = -1;
            var uncertainUntil = uncertain ? int.MaxValue : -1;
            var keptJumps = new HashSet<int>();
            int? regionSlot = null;
            var regionSlotUntil = -1;
            Value? possibleReturn = null;

            for (var index = 0; index < tree.Length; index++)
            {
                if (++_steps > MaxSteps) return null;

                var instruction = tree[index];
                var unsure = index < uncertainUntil;
                if (index >= regionSlotUntil) regionSlot = null;

                switch (instruction.Op)
                {
                    case 0x01: stack.Add(new LocalRef(instruction.Operand)); break;
                    case 0x02: declaredLocal = instruction.Operand; break;
                    case 0x09: stack.Add(new Text(type.Text(instruction.Operand) ?? string.Empty)); break;
                    case 0x0A: stack.Add(new Number(ToDouble(instruction.FloatOperand), false)); break;
                    case 0x0B: stack.Add(new Number(instruction.Operand, true)); break;
                    case 0x0C: stack.Add(NullValue); break;
                    case 0x0D: stack.Add(UnknownValue); break;
                    case 0x0E:
                        var constant = instruction.Operand >= 0 && instruction.Operand < type.Pool.Count ? type.Pool[instruction.Operand] : default;
                        stack.Add(constant.Kind == SlrrConstantKind.Resource && type.Text(constant.A) is { } rpk
                            ? new Resource(rpk, constant.B)
                            : UnknownValue);
                        break;

                    case 0x04:
                    case 0x05:
                    {
                        // a && b: a, 04, jump past b, b, and. The jump is only taken when a settles the result
                        if (index + 1 >= tree.Length || tree[index + 1].Op != 0x14) break;

                        var first = stack.Count > 0 ? Truth(Resolve(stack[^1], locals)) : null;
                        var settled = instruction.Op == 0x04 ? first == false : first == true;
                        index += settled ? tree[index + 1].Operand : 1;
                        break;
                    }

                    case 0x07:
                        Operator(instruction.Operand, stack, locals, unsure);
                        break;

                    case 0x08:
                        Statement(instruction.Operand, stack, locals, unsure);
                        stack.Clear();
                        break;

                    case 0x10:
                        return stack.Count > 0 ? Resolve(stack[^1], locals) : null;

                    case 0x11: stack.Add(new Marker(instruction.Operand)); break;

                    case 0x12:
                    case 0x1A:
                    {
                        var name = instruction.Op == 0x12 ? type.Text(instruction.Operand) : type.MemberName(instruction.Operand);
                        var foreign = TakePath(stack);
                        var callArguments = TakeArguments(stack, locals);

                        var owner = instruction.Op == 0x1A ? type.MemberClass(instruction.Operand) : null;
                        if (foreign || name == null || owner != null && !_chainNames.Contains(owner))
                            stack.Add(UnknownValue);
                        else
                            stack.Add(CallSelf(name, callArguments, unsure, depth));
                        break;
                    }

                    case 0x26:
                    {
                        var name = type.Text(instruction.Operand) ?? type.MemberName(instruction.Operand);
                        var callArguments = TakeArguments(stack, locals);
                        stack.Add(name == null ? UnknownValue : InvokeVirtual(classIndex + 1, name, callArguments, unsure, depth));
                        break;
                    }

                    case 0x25:
                        if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                        TakeArguments(stack, locals);
                        stack.Add(UnknownValue);
                        break;

                    case 0x21:
                        TakeArguments(stack, locals);
                        if (stack.Count > 0 && stack[^1] is NewObject) stack.RemoveAt(stack.Count - 1);
                        stack.Add(UnknownValue);
                        break;

                    case 0x22:
                    case 0x23:
                        TakeArguments(stack, locals);
                        stack.Add(UnknownValue);
                        break;

                    case 0x19: stack.Add(UnknownValue); break;

                    case 0x1B:
                    {
                        var name = type.MemberName(instruction.Operand);
                        var owner = type.MemberClass(instruction.Operand);
                        if (TakePath(stack) || name == null)
                            stack.Add(UnknownValue);
                        else
                            // Inside an object path (the_car.make) the first field is still one of ours
                            stack.Add(owner == null || _chainNames.Contains(owner) || stack.Count > 0 && stack[^1] is Marker
                                ? new FieldRef(name)
                                : UnknownValue);
                        break;
                    }

                    case 0x20:
                    {
                        var array = Pop(stack);
                        var position = Pop(stack);
                        stack.Add(new ElementRef(array, position));
                        break;
                    }

                    case 0x27: stack.Add(new ArgumentCount(instruction.Operand)); break;

                    case 0x15:
                    {
                        var tested = Resolve(Pop(stack), locals);
                        var condition = Truth(tested);
                        stack.Clear();

                        if (condition == false)
                        {
                            if (instruction.Operand > 0) index += instruction.Operand - 1;
                        }
                        else if (condition == null && instruction.Operand > 0)
                        {
                            // Both branches get walked, neither takes effect
                            var target = index + instruction.Operand;
                            uncertainUntil = Math.Max(uncertainUntil, target);
                            regionSlot = (tested as Unknown)?.Slot;
                            regionSlotUntil = target;

                            var elseJump = target - 1;
                            if (elseJump > index + 1 && elseJump < tree.Length && tree[elseJump].Op == 0x14 && tree[elseJump].Operand > 0
                                && tree[elseJump - 1].Op is not (0x04 or 0x05))
                            {
                                keptJumps.Add(elseJump);
                                uncertainUntil = Math.Max(uncertainUntil, elseJump + tree[elseJump].Operand);
                            }
                        }
                        break;
                    }

                    case 0x14:
                        // Backwards = loop; a loop whose condition is unknown gets walked once
                        if (keptJumps.Contains(index) || instruction.Operand <= 0 && unsure) break;

                        index += instruction.Operand - 1;
                        break;

                    case 0x16:
                    case 0x17:
                        stack.Clear();
                        uncertainUntil = int.MaxValue;
                        break;

                    case 0x28:
                    {
                        var result = Resolve(Pop(stack), locals);
                        stack.Clear();

                        if (!unsure) return result;

                        // May or may not have returned here: nothing after this point is certain
                        uncertainUntil = int.MaxValue;
                        if (possibleReturn is not Unknown { Slot: not null }) possibleReturn = result is Unknown ? result : possibleReturn ?? UnknownValue;
                        if (result is Text message && regionSlot != null)
                        {
                            Rules.Add(new SlrrSlotRule(regionSlot.Value, message.Content));
                            regionSlot = null;
                        }
                        break;
                    }

                    case 0x29:
                        if (!unsure) return UnknownValue;
                        uncertainUntil = int.MaxValue;
                        break;

                    case 0x2A:
                        return possibleReturn ?? UnknownValue;

                    case 0x2D:
                        if (declaredLocal >= 0 && stack.Count > 0)
                            Assign(new LocalRef(declaredLocal), Resolve(stack[^1], locals), locals, unsure);
                        stack.Clear();
                        break;
                }
            }

            return possibleReturn;
        }

        #region Stack helpers

        private static Value Pop(List<Value> stack)
        {
            if (stack.Count == 0) return UnknownValue;

            var value = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            return value;
        }

        /// <summary>
        /// An object path announced by a marker (the_car.make = marker 2, the_car) sits on top of the stack
        /// when its member is reached. Returns true when the member belongs to another object.
        /// </summary>
        private static bool TakePath(List<Value> stack)
        {
            var marker = stack.FindLastIndex(v => v is Marker);
            if (marker < 0) return false;

            var length = ((Marker)stack[marker]).Length;
            if (stack.Count - 1 - marker != length - 1) return false;
            if (stack.Skip(marker + 1).Any(v => v is ArgumentCount)) return false;

            stack.RemoveRange(marker, stack.Count - marker);
            return length > 1;
        }

        private Value[] TakeArguments(List<Value> stack, Dictionary<int, Value> locals)
        {
            if (stack.Count == 0 || stack[^1] is not ArgumentCount count) return Array.Empty<Value>();

            stack.RemoveAt(stack.Count - 1);
            var taken = Math.Min(count.Count, stack.Count);
            var arguments = stack.Skip(stack.Count - taken).Select(v => Resolve(v, locals)).ToArray();
            stack.RemoveRange(stack.Count - taken, taken);
            return arguments;
        }

        private Value Resolve(Value value, Dictionary<int, Value> locals)
        {
            switch (value)
            {
                case FieldRef field:
                    return _fields.TryGetValue(field.Name, out var fieldValue) ? fieldValue : UnknownValue;
                case LocalRef local:
                    return locals.TryGetValue(local.Index, out var localValue) ? localValue : UnknownValue;
                case ElementRef element:
                    return Resolve(element.Array, locals) is ArrayValue array
                           && Resolve(element.Index, locals) is Number { IsInteger: true } position
                           && array.Items.TryGetValue((int)position.Amount, out var item)
                        ? item
                        : UnknownValue;
                case Marker or ArgumentCount or NewObject:
                    return UnknownValue;
                default:
                    return value;
            }
        }

        private void Assign(Value target, Value value, Dictionary<int, Value> locals, bool unsure)
        {
            // What an unknown value was read from stays known, so later checks on it can still be attributed
            if (unsure && value is not Unknown) value = UnknownValue;

            switch (target)
            {
                case LocalRef local:
                    locals[local.Index] = value;
                    break;
                case FieldRef field when !unsure:
                    _fields[field.Name] = value;
                    break;
                case ElementRef element when !unsure:
                    if (Resolve(element.Array, locals) is ArrayValue array && Resolve(element.Index, locals) is Number { IsInteger: true } position)
                        array.Items[(int)position.Amount] = value;
                    break;
            }
        }

        private static bool? Truth(Value value) => value switch
        {
            Number number => number.Amount != 0,
            Null => false,
            Unknown => null,
            _ => true
        };

        private static double ToDouble(float value) =>
            double.Parse(value.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        #endregion

        #region Operators

        private void Statement(int kind, List<Value> stack, Dictionary<int, Value> locals, bool unsure)
        {
            if (kind is 23 or 24 && stack.Count > 0)
            {
                var counter = Pop(stack);
                Assign(counter, Binary(kind == 23 ? 16 : 15, Resolve(counter, locals), new Number(1, true)), locals, unsure);
                return;
            }

            if (kind is not (35 or 36 or 37 or 39 or 40) || stack.Count < 2) return;

            var target = Pop(stack);
            var value = Resolve(Pop(stack), locals);
            if (kind != 35)
            {
                var operation = kind switch { 36 => 19, 37 => 18, 39 => 16, _ => 15 };
                value = Binary(operation, Resolve(target, locals), value);
            }

            Assign(target, value, locals, unsure);
        }

        private void Operator(int kind, List<Value> stack, Dictionary<int, Value> locals, bool unsure)
        {
            switch (kind)
            {
                case 27 or 28: // call result, literal
                    break;

                case 30:
                    stack.Add(new NewObject());
                    break;

                case 31:
                    var size = Resolve(Pop(stack), locals);
                    stack.Add(size is Number { IsInteger: true } length ? new ArrayValue((int)length.Amount) : UnknownValue);
                    break;

                case 35: // assignment used as a value
                {
                    var target = Pop(stack);
                    var value = Resolve(Pop(stack), locals);
                    Assign(target, value, locals, unsure);
                    stack.Add(unsure ? UnknownValue : value);
                    break;
                }

                case 23 or 24 or 25 or 26: // i++, i--, ++i, --i
                {
                    var target = Pop(stack);
                    var before = Resolve(target, locals);
                    var after = Binary(kind is 23 or 25 ? 16 : 15, before, new Number(1, true));
                    Assign(target, after, locals, unsure);
                    stack.Add(kind is 23 or 24 ? before : after);
                    break;
                }

                case 20 or 21 or 22 or 32 or 33:
                {
                    var operand = Resolve(Pop(stack), locals);
                    stack.Add(Carry(kind switch
                    {
                        21 => Truth(operand) is { } truth ? new Number(truth ? 0 : 1, true) : UnknownValue,
                        22 => operand is Number number ? new Number(-number.Amount, number.IsInteger) : UnknownValue,
                        20 => operand is Number { IsInteger: true } bits ? new Number(~(long)bits.Amount, true) : UnknownValue,
                        33 => operand,
                        _ => UnknownValue
                    }, operand));
                    break;
                }

                default:
                    var right = Resolve(Pop(stack), locals);
                    var left = Resolve(Pop(stack), locals);
                    stack.Add(Carry(Binary(kind, left, right), left, right));
                    break;
            }
        }

        /// <summary>An unknown result keeps pointing at the slot one of its operands came from</summary>
        private static Value Carry(Value result, params Value[] operands) =>
            result is Unknown { Slot: null }
                ? operands.FirstOrDefault(o => o is Unknown { Slot: not null }) ?? result
                : result;

        private static Value Binary(int kind, Value left, Value right)
        {
            // Logic that one known side settles
            if (kind is 1 or 2)
            {
                var (a, b) = (Truth(left), Truth(right));
                if (kind == 2 && (a == false || b == false)) return new Number(0, true);
                if (kind == 1 && (a == true || b == true)) return new Number(1, true);
                return a == null || b == null ? UnknownValue : new Number(kind == 2 ? 1 : 0, true);
            }

            if (kind is 6 or 7)
            {
                bool? equal = (left, right) switch
                {
                    (Unknown, _) or (_, Unknown) => null,
                    (Number a, Number b) => a.Amount == b.Amount,
                    (Null, Null) => true,
                    (Text a, Text b) => a.Content == b.Content,
                    (Resource a, Resource b) => a == b,
                    _ => false
                };
                return equal == null ? UnknownValue : new Number(equal == (kind == 7) ? 1 : 0, true);
            }

            if (kind == 16 && (left is Text || right is Text))
            {
                return Display(left) is { } a && Display(right) is { } b ? new Text(a + b) : UnknownValue;
            }

            if (left is not Number x || right is not Number y) return UnknownValue;

            var integers = x.IsInteger && y.IsInteger;
            switch (kind)
            {
                case 8: return new Number(x.Amount >= y.Amount ? 1 : 0, true);
                case 9: return new Number(x.Amount <= y.Amount ? 1 : 0, true);
                case 10: return new Number(x.Amount > y.Amount ? 1 : 0, true);
                case 11: return new Number(x.Amount < y.Amount ? 1 : 0, true);
                case 15: return new Number(x.Amount - y.Amount, integers);
                case 16: return new Number(x.Amount + y.Amount, integers);
                case 19: return new Number(x.Amount * y.Amount, integers);
                case 18:
                    if (y.Amount == 0) return UnknownValue;
                    return integers ? new Number(Math.Truncate(x.Amount / y.Amount), true) : new Number(x.Amount / y.Amount, false);
                case 17:
                    return y.Amount == 0 ? UnknownValue : new Number(x.Amount % y.Amount, integers);
            }

            if (!integers) return UnknownValue;

            var (p, q) = ((long)x.Amount, (long)y.Amount);
            return kind switch
            {
                3 => new Number(p | q, true),
                4 => new Number(p ^ q, true),
                5 => new Number(p & q, true),
                13 => new Number(p >> (int)q, true),
                14 => new Number((int)(p << (int)q), true),
                _ => UnknownValue
            };
        }

        private static string? Display(Value value) => value switch
        {
            Text text => text.Content,
            Number number => number.IsInteger
                ? ((long)number.Amount).ToString(CultureInfo.InvariantCulture)
                : number.Amount.ToString(CultureInfo.InvariantCulture),
            _ => null
        };

        #endregion
    }
}
