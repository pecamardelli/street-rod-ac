using System.Globalization;

namespace Street_Rod_AC.Parts.Scripting;

/// <summary>"Nothing works without a part on this slot", found by running a check method; with the script's own words</summary>
public sealed record ScriptSlotRule(int Slot, string Message);

/// <summary>
/// Interpreter for the compiled part scripts of the source game (see <see cref="ScriptClass"/>): objects with
/// fields, virtual calls along the class chain, statics, arrays and the usual expressions and control flow.
/// What the source game did natively is up to the <see cref="IScriptHost"/>. Whatever the host does not
/// provide evaluates to "unknown"; branches on unknown conditions are walked without taking effect, except for
/// what they reveal: a check that returns a message after testing an unknown part is a <see cref="ScriptSlotRule"/>.
/// </summary>
public sealed class ScriptVm
{
    private const int MaxDepth = 24;
    private const int MaxSteps = 500_000;
    private const string Constructor = "<init>";
    private const string PartConstructorSignature = "(I)";

    private readonly IScriptHost _host;
    private readonly Dictionary<string, ScriptObject?> _statics = new();
    private int _steps;

    public ScriptVm(ScriptClassLoader loader, IScriptHost host)
    {
        Loader = loader;
        _host = host;
    }

    public ScriptClassLoader Loader { get; }

    public List<ScriptSlotRule> Rules { get; } = new();

    /// <summary>Creates an object: field initializers and constructor of every class of the chain, base classes first</summary>
    public ScriptObject Instantiate(IReadOnlyList<ScriptClass> chain, params ScriptValue[] arguments)
    {
        _steps = 0;
        return Construct(chain, arguments, 0);
    }

    /// <summary>Calls a method by name and argument count; unknown when the object has no such method</summary>
    public ScriptValue Call(ScriptObject target, string method, params ScriptValue[] arguments)
    {
        _steps = 0;
        return InvokeVirtual(target, 0, method, arguments, false, 0);
    }

    #region Objects and calls

    private ScriptObject Construct(IReadOnlyList<ScriptClass> chain, ScriptValue[] arguments, int depth)
    {
        var instance = new ScriptObject(chain);
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            InitializeFields(instance, i, false);

            var constructors = chain[i].Methods.Where(m => m.Name == Constructor).ToList();
            var constructor = i == 0 ? constructors.FirstOrDefault(m => m.ParameterCount == arguments.Length) : null;
            constructor ??= constructors.FirstOrDefault(m => m.Signature == PartConstructorSignature)
                            ?? constructors.FirstOrDefault(m => m.ParameterCount == 0)
                            ?? constructors.FirstOrDefault();
            if (constructor == null) continue;

            var passed = constructor.ParameterCount == arguments.Length
                ? arguments
                : Enumerable.Repeat(ScriptValue.Unknown, constructor.ParameterCount).ToArray();
            Invoke(instance, i, constructor, passed, false, depth);
        }

        return instance;
    }

    private void InitializeFields(ScriptObject instance, int classIndex, bool staticsOnly)
    {
        var type = instance.Chain[classIndex];
        foreach (var field in type.Fields)
        {
            if (staticsOnly && !field.IsStatic) continue;

            if (field.Tree < 0 || field.Tree >= type.Trees.Count)
            {
                instance.Fields[field.Name] = field.Signature switch
                {
                    "F" or "D" => new ScriptNumber(0, false),
                    "I" or "Z" or "B" or "S" or "C" or "J" => new ScriptNumber(0, true),
                    _ => ScriptValue.Null
                };
                continue;
            }

            var value = Run(instance, classIndex, type.Trees[field.Tree], Array.Empty<ScriptValue>(), false, false, 0);
            if (value != null) instance.Fields[field.Name] = value;
        }
    }

    /// <summary>Stand-in object for the static side of a class; null when the class is not a script class</summary>
    private ScriptObject? Statics(string className, string? nearFolder)
    {
        if (_statics.TryGetValue(className, out var cached)) return cached;

        ScriptObject? holder = null;
        if (Loader.Chain(className, nearFolder) is { } chain)
        {
            _statics[className] = holder = new ScriptObject(chain);
            for (var i = chain.Count - 1; i >= 0; i--) InitializeFields(holder, i, true);
        }

        return _statics[className] = holder;
    }

    private ScriptValue InvokeVirtual(ScriptObject target, int fromClass, string name, ScriptValue[] arguments, bool uncertain, int depth)
    {
        if (_host is IScriptInterceptor interceptor && interceptor.Intercept(this, target, name, arguments, uncertain) is { } intercepted)
            return intercepted;

        for (var i = fromClass; i < target.Chain.Count; i++)
        {
            var method = target.Chain[i].Methods.FirstOrDefault(m => m.Name == name && m.ParameterCount == arguments.Length);
            if (method == null) continue;
            if (method.IsNative || method.Tree < 0 || method.Tree >= target.Chain[i].Trees.Count) break;

            return Invoke(target, i, method, arguments, uncertain, depth);
        }

        return _host.CallNative(this, target, name, arguments, uncertain) ?? ScriptValue.Unknown;
    }

    private ScriptValue Invoke(ScriptObject target, int classIndex, ScriptMethod method, ScriptValue[] arguments, bool uncertain, int depth)
    {
        var type = target.Chain[classIndex];
        if (depth > MaxDepth || method.Tree < 0 || method.Tree >= type.Trees.Count) return ScriptValue.Unknown;

        return Run(target, classIndex, type.Trees[method.Tree], arguments, !method.IsStatic, uncertain, depth + 1) ?? ScriptValue.Unknown;
    }

    private ScriptValue CallOn(ScriptValue? target, ScriptObject self, ScriptClass type, string name, ScriptValue[] arguments, bool uncertain, int depth)
    {
        switch (target)
        {
            case ScriptReference reference:
                return InvokeVirtual(reference.Target, 0, name, arguments, uncertain, depth);

            case ScriptClassReference classReference:
                return Statics(classReference.ClassName, type.Folder) is { } statics
                    ? InvokeVirtual(statics, 0, name, arguments, uncertain, depth)
                    : _host.CallStatic(this, classReference.ClassName, name, arguments) ?? ScriptValue.Unknown;

            case null:
                return InvokeVirtual(self, 0, name, arguments, uncertain, depth);

            default:
                return target is ScriptUnknown ? target : ScriptValue.Unknown;
        }
    }

    #endregion

    #region Stack tokens

    private sealed record LocalRef(int Index) : ScriptValue;
    private sealed record FieldRef(ScriptObject Owner, string Name) : ScriptValue;
    private sealed record PathField(string Name) : ScriptValue;
    private sealed record ElementRef(ScriptValue Array, ScriptValue Index) : ScriptValue;
    private sealed record Marker(int Length) : ScriptValue;
    private sealed record ArgumentCount(int Count) : ScriptValue;
    private sealed record NewObject(string? ClassName) : ScriptValue;

    #endregion

    /// <summary>
    /// Executes one tree. <paramref name="uncertain"/> means the caller does not know whether this code
    /// really runs, so nothing may change state. Returns the value of the first return (or initializer end).
    /// </summary>
    private ScriptValue? Run(ScriptObject self, int classIndex, ScriptInstruction[] tree, ScriptValue[] arguments, bool hasThis, bool uncertain, int depth)
    {
        var type = self.Chain[classIndex];
        var stack = new List<ScriptValue>();
        var locals = new Dictionary<int, ScriptValue>();

        // Parameters are numbered from the last one back; local 0 is "this" when there is one
        for (var i = 0; i < arguments.Length; i++) locals[arguments.Length - 1 - i + (hasThis ? 1 : 0)] = arguments[i];
        if (hasThis) locals[0] = new ScriptReference(self);

        var declaredLocal = -1;
        var uncertainUntil = uncertain ? int.MaxValue : -1;
        var keptJumps = new HashSet<int>();
        int? regionSlot = null;
        var regionSlotUntil = -1;
        ScriptValue? possibleReturn = null;

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
                case 0x09: stack.Add(new ScriptText(type.Text(instruction.Operand) ?? string.Empty)); break;
                case 0x0A: stack.Add(new ScriptNumber(ToDouble(instruction.FloatOperand), false)); break;
                case 0x0B: stack.Add(new ScriptNumber(instruction.Operand, true)); break;
                case 0x0C: stack.Add(ScriptValue.Null); break;
                case 0x0D: stack.Add(ScriptValue.Unknown); break;
                case 0x0E:
                    var constant = instruction.Operand >= 0 && instruction.Operand < type.Pool.Count ? type.Pool[instruction.Operand] : default;
                    stack.Add(constant.Kind == ScriptConstantKind.Resource && type.Text(constant.A) is { } rpk
                        ? new ScriptResource(rpk, constant.B)
                        : ScriptValue.Unknown);
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
                {
                    // instanceof, cast and new are followed by their type, which is not an instruction of its own
                    var typeName = instruction.Operand is 30 or 32 or 33 ? NextType(type, tree, index) : null;
                    Operator(instruction.Operand, typeName, stack, locals, unsure);
                    if (typeName != null) index++;
                    break;
                }

                case 0x1C:
                {
                    // Field of whatever the expression before produced: ((EnginePart)p).the_car
                    var name = type.Text(instruction.Operand);
                    var owner = Resolve(Pop(stack), locals);
                    stack.Add(owner is ScriptReference reference && name != null
                        ? new FieldRef(reference.Target, name)
                        : owner is ScriptUnknown ? owner : ScriptValue.Unknown);
                    break;
                }

                case 0x08:
                    Statement(instruction.Operand, stack, locals, unsure);
                    stack.Clear();
                    break;

                case 0x10:
                    return stack.Count > 0 ? Resolve(stack[^1], locals) : null;

                case 0x11: stack.Add(new Marker(instruction.Operand)); break;

                case 0x19:
                    stack.Add(type.ClassAt(instruction.Operand) is { } referenced ? new ScriptClassReference(referenced) : ScriptValue.Unknown);
                    break;

                case 0x12:
                case 0x1A:
                {
                    var name = instruction.Op == 0x12 ? type.Text(instruction.Operand) : type.MemberName(instruction.Operand);
                    var hasPath = TakePath(self, stack, locals, out var target);
                    var callArguments = TakeArguments(stack, locals);

                    // No path: our own method, or a static one of a class we do not descend from
                    if (!hasPath && instruction.Op == 0x1A && type.MemberClass(instruction.Operand) is { } owner && !self.Is(owner))
                        target = new ScriptClassReference(owner);

                    stack.Add(name == null ? ScriptValue.Unknown : CallOn(target, self, type, name, callArguments, unsure, depth));
                    break;
                }

                case 0x25:
                {
                    // Method of whatever the expression before produced: list.elementAt(i).intValue()
                    var target = Resolve(Pop(stack), locals);
                    var callArguments = TakeArguments(stack, locals);
                    var name = type.Text(instruction.Operand) ?? type.MemberName(instruction.Operand);
                    stack.Add(name == null ? ScriptValue.Unknown : CallOn(target, self, type, name, callArguments, unsure, depth));
                    break;
                }

                case 0x26:
                {
                    var name = type.Text(instruction.Operand) ?? type.MemberName(instruction.Operand);
                    var callArguments = TakeArguments(stack, locals);
                    stack.Add(name == null ? ScriptValue.Unknown : InvokeVirtual(self, classIndex + 1, name, callArguments, unsure, depth));
                    break;
                }

                case 0x21:
                {
                    var callArguments = TakeArguments(stack, locals);
                    var created = Pop(stack) as NewObject;
                    var chain = created?.ClassName == null ? null : Loader.Chain(created.ClassName, type.Folder);
                    stack.Add(chain == null || depth > MaxDepth ? ScriptValue.Unknown : new ScriptReference(Construct(chain, callArguments, depth + 1)));
                    break;
                }

                case 0x22:
                case 0x23:
                    // this(...) and super(...): construction already walks the chain
                    TakeArguments(stack, locals);
                    stack.Add(ScriptValue.Unknown);
                    break;

                case 0x1B:
                {
                    var name = type.MemberName(instruction.Operand);
                    if (name == null)
                    {
                        stack.Add(ScriptValue.Unknown);
                    }
                    else if (PathIsOpen(stack))
                    {
                        stack.Add(new PathField(name));
                    }
                    else if (TakePath(self, stack, locals, out var target))
                    {
                        stack.Add(target switch
                        {
                            ScriptReference reference => new FieldRef(reference.Target, name),
                            ScriptClassReference classReference => StaticField(classReference.ClassName, name, type.Folder),
                            ScriptUnknown => target,
                            _ => ScriptValue.Unknown
                        });
                    }
                    else
                    {
                        var owner = type.MemberClass(instruction.Operand);
                        stack.Add(owner == null || self.Is(owner) ? new FieldRef(self, name) : StaticField(owner, name, type.Folder));
                    }
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
                        regionSlot = (tested as ScriptUnknown)?.Slot;
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
                    if (possibleReturn is not ScriptUnknown { Slot: not null })
                        possibleReturn = result is ScriptUnknown ? result : possibleReturn ?? ScriptValue.Unknown;
                    if (result is ScriptText message && regionSlot != null)
                    {
                        Rules.Add(new ScriptSlotRule(regionSlot.Value, message.Content));
                        regionSlot = null;
                    }
                    break;
                }

                case 0x29:
                    if (!unsure) return ScriptValue.Unknown;
                    uncertainUntil = int.MaxValue;
                    break;

                case 0x2A:
                    return possibleReturn ?? ScriptValue.Unknown;

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

    private static ScriptValue Pop(List<ScriptValue> stack)
    {
        if (stack.Count == 0) return ScriptValue.Unknown;

        var value = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return value;
    }

    /// <summary>Type operand that follows instanceof, cast and new: a signature string or a class constant</summary>
    private static string? NextType(ScriptClass type, ScriptInstruction[] tree, int index)
    {
        if (index + 1 >= tree.Length) return null;

        var next = tree[index + 1];
        return next.Op switch
        {
            0x06 => type.TypeName(next.Operand),
            0x19 => type.ClassAt(next.Operand),
            _ => null
        };
    }

    /// <summary>A marker announces an object path of Length - 1 elements (the_car.make = marker 2, the_car, then the member)</summary>
    private static bool PathIsOpen(List<ScriptValue> stack)
    {
        var marker = stack.FindLastIndex(v => v is Marker);
        return marker >= 0 && stack.Count - 1 - marker < ((Marker)stack[marker]).Length - 1;
    }

    /// <summary>
    /// Takes a complete object path off the stack and follows it. False when the member that comes next
    /// is one of our own (no path, or the "this" path of marker 1).
    /// </summary>
    private bool TakePath(ScriptObject self, List<ScriptValue> stack, Dictionary<int, ScriptValue> locals, out ScriptValue? target)
    {
        target = null;

        var marker = stack.FindLastIndex(v => v is Marker);
        if (marker < 0) return false;

        var length = ((Marker)stack[marker]).Length;
        if (stack.Count - 1 - marker != length - 1) return false;
        if (stack.Skip(marker + 1).Any(v => v is ArgumentCount)) return false;

        var elements = stack.Skip(marker + 1).ToList();
        stack.RemoveRange(marker, stack.Count - marker);
        if (elements.Count == 0) return false;

        var current = elements[0] is PathField first ? Resolve(new FieldRef(self, first.Name), locals) : Resolve(elements[0], locals);
        foreach (var element in elements.Skip(1))
        {
            current = (current, element) switch
            {
                (ScriptReference reference, PathField field) => Resolve(new FieldRef(reference.Target, field.Name), locals),
                (ScriptClassReference classReference, PathField field) => StaticField(classReference.ClassName, field.Name, self.Chain[0].Folder),
                (ScriptUnknown, _) => current,
                _ => ScriptValue.Unknown
            };
        }

        target = current;
        return true;
    }

    private ScriptValue StaticField(string className, string field, string? nearFolder) =>
        Statics(className, nearFolder) is { } statics && statics.Fields.TryGetValue(field, out var value) ? value : ScriptValue.Unknown;

    private ScriptValue[] TakeArguments(List<ScriptValue> stack, Dictionary<int, ScriptValue> locals)
    {
        if (stack.Count == 0 || stack[^1] is not ArgumentCount count) return Array.Empty<ScriptValue>();

        stack.RemoveAt(stack.Count - 1);
        var taken = Math.Min(count.Count, stack.Count);
        var arguments = stack.Skip(stack.Count - taken).Select(v => Resolve(v, locals)).ToArray();
        stack.RemoveRange(stack.Count - taken, taken);
        return arguments;
    }

    private static ScriptValue Resolve(ScriptValue value, Dictionary<int, ScriptValue> locals)
    {
        switch (value)
        {
            case FieldRef field:
                return field.Owner.Fields.TryGetValue(field.Name, out var fieldValue) ? fieldValue : ScriptValue.Unknown;
            case LocalRef local:
                return locals.TryGetValue(local.Index, out var localValue) ? localValue : ScriptValue.Unknown;
            case ElementRef element:
                return Resolve(element.Array, locals) is ScriptArray array
                       && Resolve(element.Index, locals) is ScriptNumber { IsInteger: true } position
                       && array.Items.TryGetValue((int)position.Amount, out var item)
                    ? item
                    : ScriptValue.Unknown;
            case Marker or ArgumentCount or NewObject or PathField:
                return ScriptValue.Unknown;
            default:
                return value;
        }
    }

    private static void Assign(ScriptValue target, ScriptValue value, Dictionary<int, ScriptValue> locals, bool unsure)
    {
        // What an unknown value was read from stays known, so later checks on it can still be attributed
        var original = value;
        if (unsure && value is not ScriptUnknown) value = ScriptValue.Unknown;

        switch (target)
        {
            case LocalRef local:
                locals[local.Index] = value;
                break;

            case FieldRef field when !unsure:
                field.Owner.Fields[field.Name] = value;
                break;

            // Lists of parts filled inside random branches (one engine or another): the first alternative wins
            case FieldRef field when original is ScriptArray && (!field.Owner.Fields.TryGetValue(field.Name, out var existing) || existing is ScriptNull):
                field.Owner.Fields[field.Name] = original;
                break;

            case ElementRef element when !unsure || original is ScriptResource:
                if (Resolve(element.Array, locals) is ScriptArray array && Resolve(element.Index, locals) is ScriptNumber { IsInteger: true } position
                    && (!unsure || !array.Items.ContainsKey((int)position.Amount)))
                    array.Items[(int)position.Amount] = original;
                break;
        }
    }

    private static bool? Truth(ScriptValue value) => value switch
    {
        ScriptNumber number => number.Amount != 0,
        ScriptNull => false,
        ScriptUnknown => null,
        _ => true
    };

    /// <summary>Script literals are floats; 2.66 should not turn into 2.6600000858</summary>
    private static double ToDouble(float value) =>
        double.Parse(value.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    #endregion

    #region Operators

    private static void Statement(int kind, List<ScriptValue> stack, Dictionary<int, ScriptValue> locals, bool unsure)
    {
        if (kind is 23 or 24 && stack.Count > 0)
        {
            var counter = Pop(stack);
            Assign(counter, Binary(kind == 23 ? 16 : 15, Resolve(counter, locals), new ScriptNumber(1, true)), locals, unsure);
            return;
        }

        if (kind is not (35 or 36 or 37 or 39 or 40 or 46) || stack.Count < 2) return;

        var target = Pop(stack);
        var value = Resolve(Pop(stack), locals);
        if (kind != 35)
        {
            var operation = kind switch { 36 => 19, 37 => 18, 39 => 16, 46 => 3, _ => 15 };
            value = Binary(operation, Resolve(target, locals), value);
        }

        Assign(target, value, locals, unsure);
    }

    private static void Operator(int kind, string? typeName, List<ScriptValue> stack, Dictionary<int, ScriptValue> locals, bool unsure)
    {
        switch (kind)
        {
            case 27 or 28: // call result, literal
                break;

            case 30:
                stack.Add(new NewObject(typeName));
                break;

            case 31:
                var size = Resolve(Pop(stack), locals);
                stack.Add(size is ScriptNumber { IsInteger: true } length ? new ScriptArray((int)length.Amount) : ScriptValue.Unknown);
                break;

            case 35: // assignment used as a value
            {
                var target = Pop(stack);
                var value = Resolve(Pop(stack), locals);
                Assign(target, value, locals, unsure);
                stack.Add(unsure && value is not ScriptUnknown ? ScriptValue.Unknown : value);
                break;
            }

            case 23 or 24 or 25 or 26: // i++, i--, ++i, --i
            {
                var target = Pop(stack);
                var before = Resolve(target, locals);
                var after = Binary(kind is 23 or 25 ? 16 : 15, before, new ScriptNumber(1, true));
                Assign(target, after, locals, unsure);
                stack.Add(kind is 23 or 24 ? before : after);
                break;
            }

            case 20 or 21 or 22 or 32 or 33:
            {
                var operand = Resolve(Pop(stack), locals);
                stack.Add(Carry(kind switch
                {
                    21 => Truth(operand) is { } truth ? new ScriptNumber(truth ? 0 : 1, true) : ScriptValue.Unknown,
                    22 => operand is ScriptNumber number ? new ScriptNumber(-number.Amount, number.IsInteger) : ScriptValue.Unknown,
                    20 => operand is ScriptNumber { IsInteger: true } bits ? new ScriptNumber(~(long)bits.Amount, true) : ScriptValue.Unknown,
                    32 => operand switch
                    {
                        ScriptReference reference when typeName != null => ScriptValue.Of(reference.Target.Is(typeName)),
                        ScriptNull => ScriptValue.Of(false),
                        _ => ScriptValue.Unknown
                    },
                    33 => operand,
                    _ => ScriptValue.Unknown
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
    private static ScriptValue Carry(ScriptValue result, params ScriptValue[] operands) =>
        result is ScriptUnknown { Slot: null }
            ? operands.FirstOrDefault(o => o is ScriptUnknown { Slot: not null }) ?? result
            : result;

    private static ScriptValue Binary(int kind, ScriptValue left, ScriptValue right)
    {
        // Logic that one known side settles
        if (kind is 1 or 2)
        {
            var (a, b) = (Truth(left), Truth(right));
            if (kind == 2 && (a == false || b == false)) return ScriptValue.Of(false);
            if (kind == 1 && (a == true || b == true)) return ScriptValue.Of(true);
            return a == null || b == null ? ScriptValue.Unknown : ScriptValue.Of(kind == 2);
        }

        if (kind is 6 or 7)
        {
            bool? equal = (left, right) switch
            {
                (ScriptUnknown, _) or (_, ScriptUnknown) => null,
                (ScriptNumber a, ScriptNumber b) => a.Amount == b.Amount,
                (ScriptNull, ScriptNull) => true,
                (ScriptText a, ScriptText b) => a.Content == b.Content,
                (ScriptResource a, ScriptResource b) => a == b,
                (ScriptReference a, ScriptReference b) => ReferenceEquals(a.Target, b.Target),
                _ => false
            };
            return equal == null ? ScriptValue.Unknown : ScriptValue.Of(equal == (kind == 7));
        }

        if (kind == 16 && (left is ScriptText || right is ScriptText))
            return Display(left) is { } a && Display(right) is { } b ? new ScriptText(a + b) : ScriptValue.Unknown;

        if (left is not ScriptNumber x || right is not ScriptNumber y) return ScriptValue.Unknown;

        var integers = x.IsInteger && y.IsInteger;
        switch (kind)
        {
            case 8: return ScriptValue.Of(x.Amount >= y.Amount);
            case 9: return ScriptValue.Of(x.Amount <= y.Amount);
            case 10: return ScriptValue.Of(x.Amount > y.Amount);
            case 11: return ScriptValue.Of(x.Amount < y.Amount);
            case 15: return new ScriptNumber(x.Amount - y.Amount, integers);
            case 16: return new ScriptNumber(x.Amount + y.Amount, integers);
            case 19: return new ScriptNumber(x.Amount * y.Amount, integers);
            case 18:
                if (y.Amount == 0) return ScriptValue.Unknown;
                return integers ? new ScriptNumber(Math.Truncate(x.Amount / y.Amount), true) : new ScriptNumber(x.Amount / y.Amount, false);
            case 17:
                return y.Amount == 0 ? ScriptValue.Unknown : new ScriptNumber(x.Amount % y.Amount, integers);
        }

        if (!integers) return ScriptValue.Unknown;

        var (p, q) = ((long)x.Amount, (long)y.Amount);
        return kind switch
        {
            3 => new ScriptNumber(p | q, true),
            4 => new ScriptNumber(p ^ q, true),
            5 => new ScriptNumber(p & q, true),
            13 => new ScriptNumber(p >> (int)q, true),
            14 => new ScriptNumber((int)(p << (int)q), true),
            _ => ScriptValue.Unknown
        };
    }

    private static string? Display(ScriptValue value) => value switch
    {
        ScriptText text => text.Content,
        ScriptNumber number => number.ToString(),
        _ => null
    };

    #endregion
}

/// <summary>A host that wants to answer some calls itself, even though the scripts implement them</summary>
public interface IScriptInterceptor
{
    ScriptValue? Intercept(ScriptVm vm, ScriptObject self, string method, ScriptValue[] arguments, bool uncertain);
}
