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

    // Text a script builds by adding strings together: a label or a message is a few dozen characters, and
    // s = s + s in a loop would double past any memory well inside the step budget
    private const int MaxTextLength = 64 * 1024;
    private const string Constructor = "<init>";
    private const string PartConstructorSignature = "(I)";

    private readonly IScriptHost _host;
    private readonly Dictionary<ScriptClass, ScriptObject> _statics = new();

    // What one Run works with, handed on from run to run: runs nest (a call inside a call), so one set per level
    private readonly Stack<RunScratch> _scratch = new();
    private int _steps;

    public ScriptVm(ScriptClassLoader loader, IScriptHost host)
    {
        Loader = loader;
        _host = host;
    }

    public ScriptClassLoader Loader { get; }

    public List<ScriptSlotRule> Rules { get; } = new();

    /// <summary>
    /// A call ran out of steps since this VM was made: what it returned stopped half way (a script that loops
    /// for ever, or one far bigger than any part needs). It stays set, so a caller can tell after the fact.
    /// </summary>
    public bool BudgetExhausted { get; private set; }

    /// <summary>
    /// For reading parts out of scripts with no game around: lists of parts are often filled inside branches nobody
    /// can decide here (one engine or another, at random). With this on, such a list and the resources put into it
    /// are kept although the branch is uncertain, and the first alternative wins. A game that runs the scripts for
    /// real leaves it off: there, uncertain code changes nothing.
    /// </summary>
    public bool FirstAlternativeWins { get; init; }

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
        var statics = Statics(chain, depth);
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            // Statics belong to the class: an instance sees them, it does not run their initializers again
            foreach (var field in chain[i].Fields)
            {
                if (field.IsStatic && statics.Fields.TryGetValue(field.Name, out var shared)) instance.Fields[field.Name] = shared;
            }

            InitializeFields(instance, i, false, depth);

            var constructors = chain[i].MethodsNamed(Constructor);
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

    /// <summary>Runs the initializers of a class: those of its static fields or those of its instance fields</summary>
    private void InitializeFields(ScriptObject instance, int classIndex, bool statics, int depth)
    {
        var type = instance.Chain[classIndex];
        foreach (var field in type.Fields)
        {
            if (field.IsStatic != statics) continue;

            if (field.Tree < 0 || field.Tree >= type.Trees.Count)
            {
                instance.Fields[field.Name] = ScriptTypes.Default(field.Signature);
                continue;
            }

            // An initializer may construct objects, whose initializers construct objects: the depth carries on
            var value = Run(instance, classIndex, type.Trees[field.Tree], Array.Empty<ScriptValue>(), false, false, depth);
            if (value != null) instance.Fields[field.Name] = ScriptTypes.Convert(value, field.Signature);
        }
    }

    /// <summary>
    /// Stand-in object for the static side of a class. It is known before its initializers run, so a class
    /// that constructs itself in one of them (static Foo instance = new Foo()) finds it instead of starting over.
    /// </summary>
    /// <remarks>
    /// A static initializer that reads a static of another class runs that class's initializers first, and so on
    /// down a chain of classes: each one is a level deeper, as a call is. Past the depth limit the class's statics
    /// are unknown for now and not remembered, so a read from higher up later still gets them right.
    /// </remarks>
    private ScriptObject Statics(IReadOnlyList<ScriptClass> chain, int depth)
    {
        if (_statics.TryGetValue(chain[0], out var holder)) return holder;
        if (depth > MaxDepth) return new ScriptObject(chain);

        _statics[chain[0]] = holder = new ScriptObject(chain);
        for (var i = chain.Count - 1; i >= 0; i--) InitializeFields(holder, i, true, depth + 1);
        return holder;
    }

    /// <summary>Null when the class is not a script class</summary>
    private ScriptObject? Statics(string className, string? nearFolder, int depth) =>
        Loader.Chain(className, nearFolder) is { } chain ? Statics(chain, depth) : null;

    private ScriptValue InvokeVirtual(ScriptObject target, int fromClass, string name, ScriptValue[] arguments, bool uncertain, int depth)
    {
        if (_host is IScriptInterceptor interceptor && interceptor.Intercept(this, target, name, arguments, uncertain) is { } intercepted)
            return intercepted;

        for (var i = fromClass; i < target.Chain.Count; i++)
        {
            var method = target.Chain[i].FindMethod(name, arguments.Length);
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

        // A float parameter handed an int is a float from there on, as is what a float method returns
        var types = method.ParameterTypes;
        var passed = new ScriptValue[arguments.Length];
        for (var i = 0; i < arguments.Length; i++) passed[i] = i < types.Count ? ScriptTypes.Convert(arguments[i], types[i]) : arguments[i];

        var result = Run(target, classIndex, type.Trees[method.Tree], passed, !method.IsStatic, uncertain, depth + 1) ?? ScriptValue.Unknown;
        return ScriptTypes.Convert(result, method.ReturnType);
    }

    private ScriptValue CallOn(ScriptValue? target, ScriptObject self, ScriptClass type, string name, ScriptValue[] arguments, bool uncertain, int depth)
    {
        switch (target)
        {
            case ScriptReference reference:
                return InvokeVirtual(reference.Target, 0, name, arguments, uncertain, depth);

            case ScriptClassReference classReference:
                return Statics(classReference.ClassName, type.Folder, depth) is { } statics
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

    /// <summary>Local variables of one call, with the types they were declared with</summary>
    private sealed class Locals : Dictionary<int, ScriptValue>
    {
        public Dictionary<int, string> Types { get; } = new();
    }

    /// <summary>The collections of one Run, emptied and kept for the next</summary>
    private sealed class RunScratch
    {
        public List<ScriptValue> Stack { get; } = new();
        public Locals Locals { get; } = new();
        public HashSet<int> KeptJumps { get; } = new();

        public void Clear()
        {
            Stack.Clear();
            Locals.Clear();
            Locals.Types.Clear();
            KeptJumps.Clear();
        }
    }

    #endregion

    /// <summary>
    /// Executes one tree. <paramref name="uncertain"/> means the caller does not know whether this code
    /// really runs, so nothing may change state. Returns the value of the first return (or initializer end).
    /// </summary>
    private ScriptValue? Run(ScriptObject self, int classIndex, ScriptInstruction[] tree, ScriptValue[] arguments, bool hasThis, bool uncertain, int depth)
    {
        var scratch = _scratch.Count > 0 ? _scratch.Pop() : new RunScratch();
        try
        {
            return Run(self, classIndex, tree, arguments, hasThis, uncertain, depth, scratch);
        }
        finally
        {
            scratch.Clear();
            _scratch.Push(scratch);
        }
    }

    private ScriptValue? Run(ScriptObject self, int classIndex, ScriptInstruction[] tree, ScriptValue[] arguments, bool hasThis, bool uncertain, int depth,
        RunScratch scratch)
    {
        var type = self.Chain[classIndex];
        var stack = scratch.Stack;
        var locals = scratch.Locals;

        // Parameters are numbered from the last one back; local 0 is "this" when there is one
        for (var i = 0; i < arguments.Length; i++) locals[arguments.Length - 1 - i + (hasThis ? 1 : 0)] = arguments[i];
        if (hasThis) locals[0] = new ScriptReference(self);

        // Code between an undecidable if and the end of its branches is walked without effect. The region has a
        // start too: a loop around it comes back to certain code, and decides the if anew on every round.
        var declaredLocal = -1;
        var uncertainFrom = uncertain ? 0 : -1;
        var uncertainUntil = uncertain ? int.MaxValue : -1;
        var keptJumps = scratch.KeptJumps;
        int? regionSlot = null;
        var regionSlotUntil = -1;
        ScriptValue? possibleReturn = null;

        for (var index = 0; index < tree.Length; index++)
        {
            if (++_steps > MaxSteps)
            {
                BudgetExhausted = true;
                return null;
            }

            var instruction = tree[index];
            var unsure = index >= uncertainFrom && index < uncertainUntil;
            if (index >= regionSlotUntil) regionSlot = null;

            switch (instruction.Op)
            {
                case 0x01: stack.Add(new LocalRef(instruction.Operand)); break;
                case 0x02:
                    // Followed by the type the local is declared with
                    declaredLocal = instruction.Operand;
                    if (index + 1 < tree.Length && tree[index + 1].Op == 0x06 && type.Text(tree[index + 1].Operand) is { } declaredType)
                        locals.Types[declaredLocal] = declaredType;
                    break;
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
                    index = settled ? Land(index + 1L + tree[index + 1].Operand, tree.Length) : index + 1;
                    break;
                }

                case 0x07:
                {
                    // instanceof, cast, new and new array are followed by their type, which is not an instruction of its own
                    var typeName = instruction.Operand is 30 or 31 or 32 or 33 ? NextType(type, tree, index) : null;
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
                    var hasPath = TakePath(self, stack, locals, depth, out var target);
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
                    else if (TakePath(self, stack, locals, depth, out var target))
                    {
                        stack.Add(target switch
                        {
                            ScriptReference reference => new FieldRef(reference.Target, name),
                            ScriptClassReference classReference => StaticField(classReference.ClassName, name, type.Folder, depth),
                            ScriptUnknown => target,
                            _ => ScriptValue.Unknown
                        });
                    }
                    else
                    {
                        var owner = type.MemberClass(instruction.Operand);
                        stack.Add(owner == null || self.Is(owner) ? new FieldRef(self, name) : StaticField(owner, name, type.Folder, depth));
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
                        if (instruction.Operand > 0) index = Land((long)index + instruction.Operand, tree.Length);
                    }
                    else if (condition == null && instruction.Operand > 0)
                    {
                        // Both branches get walked, neither takes effect. A target past the end is the end (one
                        // past it, so that the last instruction is not taken for the jump over an else)
                        var target = (int)Math.Min((long)index + instruction.Operand, tree.Length + 1L);
                        if (unsure)
                        {
                            uncertainUntil = Math.Max(uncertainUntil, target);
                        }
                        else
                        {
                            (uncertainFrom, uncertainUntil) = (index, target);
                            keptJumps.Clear();
                        }

                        regionSlot = (tested as ScriptUnknown)?.Slot;
                        regionSlotUntil = target;

                        var elseJump = target - 1;
                        if (elseJump > index + 1 && elseJump < tree.Length && tree[elseJump].Op == 0x14 && tree[elseJump].Operand > 0
                            && tree[elseJump - 1].Op is not (0x04 or 0x05))
                        {
                            keptJumps.Add(elseJump);
                            uncertainUntil = Math.Max(uncertainUntil, (int)Math.Min((long)elseJump + tree[elseJump].Operand, tree.Length + 1L));
                        }
                    }
                    break;
                }

                case 0x14:
                    // Backwards = loop; a loop whose condition is unknown gets walked once
                    if (keptJumps.Contains(index) || instruction.Operand <= 0 && unsure) break;

                    // Back to before an uncertain region: the next round meets its if afresh
                    if ((long)index + instruction.Operand < uncertainFrom && uncertainUntil != int.MaxValue)
                    {
                        (uncertainFrom, uncertainUntil) = (-1, -1);
                        keptJumps.Clear();
                    }

                    index = Land((long)index + instruction.Operand, tree.Length);
                    break;

                case 0x16:
                case 0x17:
                    stack.Clear();
                    (uncertainFrom, uncertainUntil) = (0, int.MaxValue);
                    break;

                case 0x28:
                {
                    var result = Resolve(Pop(stack), locals);
                    stack.Clear();

                    if (!unsure) return result;

                    // May or may not have returned here: nothing after this point is certain
                    (uncertainFrom, uncertainUntil) = (0, int.MaxValue);
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
                    (uncertainFrom, uncertainUntil) = (0, int.MaxValue);
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

    /// <summary>
    /// The loop index that makes <paramref name="next"/> the instruction to run next. Jump offsets come from the
    /// class file: one that lands before the start or past the end of the tree (a broken or hostile file) ends
    /// the tree, as running off its end does.
    /// </summary>
    private static int Land(long next, int length) => next >= 0 && next <= length ? (int)next - 1 : length - 1;

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
    private bool TakePath(ScriptObject self, List<ScriptValue> stack, Locals locals, int depth, out ScriptValue? target)
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
                (ScriptClassReference classReference, PathField field) => StaticField(classReference.ClassName, field.Name, self.Chain[0].Folder, depth),
                (ScriptUnknown, _) => current,
                _ => ScriptValue.Unknown
            };
        }

        target = current;
        return true;
    }

    private ScriptValue StaticField(string className, string field, string? nearFolder, int depth) =>
        Statics(className, nearFolder, depth) is { } statics && statics.Fields.TryGetValue(field, out var value) ? value : ScriptValue.Unknown;

    private ScriptValue[] TakeArguments(List<ScriptValue> stack, Locals locals)
    {
        if (stack.Count == 0 || stack[^1] is not ArgumentCount count) return Array.Empty<ScriptValue>();

        stack.RemoveAt(stack.Count - 1);
        // The count comes from the class file: never more than the stack holds, never below none
        var taken = Math.Clamp(count.Count, 0, stack.Count);
        var arguments = new ScriptValue[taken];
        for (var i = 0; i < taken; i++) arguments[i] = Resolve(stack[stack.Count - taken + i], locals);
        stack.RemoveRange(stack.Count - taken, taken);
        return arguments;
    }

    private static ScriptValue Resolve(ScriptValue value, Locals locals)
    {
        switch (value)
        {
            case FieldRef field:
                return field.Owner.Fields.TryGetValue(field.Name, out var fieldValue) ? fieldValue : ScriptValue.Unknown;
            case LocalRef local:
                return locals.TryGetValue(local.Index, out var localValue) ? localValue : ScriptValue.Unknown;
            case ElementRef element:
                return Resolve(element.Array, locals) is ScriptArray array && Resolve(element.Index, locals) is ScriptNumber { IsInteger: true } position
                    ? array.Get((int)position.Amount)
                    : ScriptValue.Unknown;
            case Marker or ArgumentCount or NewObject or PathField:
                return ScriptValue.Unknown;
            default:
                return value;
        }
    }

    private void Assign(ScriptValue target, ScriptValue value, Locals locals, bool unsure)
    {
        // What an unknown value was read from stays known, so later checks on it can still be attributed
        var original = value;
        if (unsure && value is not ScriptUnknown) value = ScriptValue.Unknown;

        switch (target)
        {
            case LocalRef local:
                locals[local.Index] = ScriptTypes.Convert(value, locals.Types.GetValueOrDefault(local.Index));
                break;

            case FieldRef field when !unsure:
                field.Owner.Fields[field.Name] = ScriptTypes.Convert(value, field.Owner.FieldSignature(field.Name));
                break;

            // Lists of parts filled inside random branches (one engine or another): the first alternative wins
            case FieldRef field when FirstAlternativeWins && original is ScriptArray && (!field.Owner.Fields.TryGetValue(field.Name, out var existing) || existing is ScriptNull):
                field.Owner.Fields[field.Name] = original;
                break;

            case ElementRef element when !unsure || FirstAlternativeWins && original is ScriptResource:
                if (Resolve(element.Array, locals) is ScriptArray array && Resolve(element.Index, locals) is ScriptNumber { IsInteger: true } position
                    && (!unsure || !array.Items.ContainsKey((int)position.Amount)))
                    array.Items[(int)position.Amount] = ScriptTypes.Convert(original, array.ElementType);
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

    private void Statement(int kind, List<ScriptValue> stack, Locals locals, bool unsure)
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

    private void Operator(int kind, string? typeName, List<ScriptValue> stack, Locals locals, bool unsure)
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
                stack.Add(size is ScriptNumber { IsInteger: true } length
                    ? new ScriptArray((int)length.Amount, typeName is { Length: > 1 } && typeName[0] == '[' ? typeName[1..] : null)
                    : ScriptValue.Unknown);
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
                    33 => ScriptTypes.Convert(operand, typeName),
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
            return Display(left) is { } a && Display(right) is { } b && (long)a.Length + b.Length <= MaxTextLength
                ? new ScriptText(a + b)
                : ScriptValue.Unknown;

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
