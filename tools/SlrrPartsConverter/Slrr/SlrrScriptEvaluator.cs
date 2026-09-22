using Street_Rod_AC.Parts.Scripting;

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

    /// <summary>Fields addStockParts() sets; car chassis fill their stock part lists there</summary>
    public Dictionary<string, object> StockState { get; init; } = new();

    public List<SlrrStockPart> StockParts { get; init; } = new();
    public List<ScriptSlotRule> RequiredSlots { get; init; } = new();

    public string? BaseClass => ClassChain.FirstOrDefault();
    public string? DisplayName => Properties.TryGetValue("name", out var name) ? name as string : null;
}

/// <summary>A part that addStockParts() mounts; conditional when it depends on the car or on random wear</summary>
public sealed record SlrrStockPart(string Rpk, int TypeId, string? Name, bool Conditional);

/// <summary>An engine kit: what a pack's Set class puts in the inventory, in the order it does</summary>
public sealed record SlrrKit(string? Name, List<(string Rpk, int TypeId)> Parts);

/// <summary>
/// Reads parts out of their compiled scripts by running them in the <see cref="ScriptVm"/> with no game around:
/// construction gives the part's properties, addStockParts() its factory parts, isDynoable()/isDriveable() the
/// slots that must be filled, updatevariables() the values the script derives.
/// </summary>
public sealed class SlrrScriptEvaluator
{
    private const string MaxWearProperty = "max_wear";
    private const string TuningBackupPrefix = "old_";

    private readonly ScriptClassLoader _loader;

    public SlrrScriptEvaluator(SlrrGame game)
    {
        _loader = new ScriptClassLoader(game.Root);
    }

    /// <summary>Static fields of every class evaluated so far, by class name</summary>
    public SortedDictionary<string, Dictionary<string, object>> Constants { get; } = new(StringComparer.Ordinal);

    /// <summary>Files of every class that took part in an evaluation: what the game needs to run the scripts itself</summary>
    public HashSet<string> UsedClassFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the script's class descends from <paramref name="className"/>; cheap, nothing is run</summary>
    public bool Extends(string scriptFile, string className)
    {
        var script = _loader.Load(scriptFile);
        return script != null && _loader.Chain(script).Skip(1).Any(c => c.ClassName == className);
    }

    /// <summary>The script's class and everything it extends, own class first; cheap, nothing is run</summary>
    public IEnumerable<string> Classes(string scriptFile)
    {
        var script = _loader.Load(scriptFile);
        return script == null ? Enumerable.Empty<string>() : _loader.Chain(script).Select(c => c.ClassName).OfType<string>();
    }

    public SlrrPartScript? Evaluate(string scriptFile)
    {
        var script = _loader.Load(scriptFile);
        if (script == null) return null;

        var chain = _loader.Chain(script);
        var host = new ExtractionHost();
        var vm = new ScriptVm(_loader, host) { FirstAlternativeWins = true };

        var part = vm.Instantiate(chain, ScriptValue.Unknown);
        var constructed = new State(part);

        // Constants and the backups tuning menus keep say nothing about the part
        var hidden = chain.SelectMany(c => c.Fields)
            .Where(f => f.IsStatic || f.Name.StartsWith(TuningBackupPrefix, StringComparison.Ordinal))
            .Select(f => f.Name)
            .ToHashSet();

        var properties = Snapshot(part, hidden);
        foreach (var type in chain)
        {
            var constants = type.Fields.Where(f => f.IsStatic && part.Fields.ContainsKey(f.Name))
                .Select(f => (f.Name, Value: Export(part.Fields[f.Name])))
                .Where(c => c.Value != null)
                .ToDictionary(c => c.Name, c => c.Value!);
            if (constants.Count > 0 && type.ClassName != null) Constants.TryAdd(type.ClassName, constants);
        }

        vm.Call(part, "addStockParts", ScriptValue.Unknown);
        var stockState = Changes(properties, Snapshot(part, hidden));
        constructed.Restore();

        // Checks are run for the "missing part" answers they give, not for what they do
        var stockPartCount = host.StockParts.Count;
        vm.Call(part, "isDynoable");
        vm.Call(part, "isDriveable");
        constructed.Restore();
        host.StockParts.RemoveRange(stockPartCount, host.StockParts.Count - stockPartCount);

        // Branches for different cars often mount the same part
        var stockParts = host.StockParts
            .GroupBy(p => (p.Rpk.ToLowerInvariant(), p.TypeId))
            .Select(g => g.First() with { Conditional = g.All(p => p.Conditional) })
            .ToList();
        var rules = vm.Rules.GroupBy(r => r.Slot).Select(g => g.First()).ToList();

        vm.Call(part, "updatevariables");
        var derived = Changes(properties, Snapshot(part, hidden));

        foreach (var file in _loader.LoadedFiles) UsedClassFiles.Add(file);

        return new SlrrPartScript
        {
            ClassName = script.ClassName,
            ClassChain = chain.Skip(1).Select(c => c.ClassName ?? string.Empty).ToList(),
            Properties = properties,
            Derived = derived,
            StockState = stockState,
            StockParts = stockParts,
            RequiredSlots = rules
        };
    }

    /// <summary>The parts a kit's build() puts in the inventory; null when the script is no class</summary>
    public SlrrKit? Kit(string scriptFile)
    {
        var script = _loader.Load(scriptFile);
        if (script == null) return null;

        var host = new KitHost();
        var vm = new ScriptVm(_loader, host);
        var kit = vm.Instantiate(_loader.Chain(script), ScriptValue.Unknown);

        // The inventory is an object of no class: whatever the kit calls on it comes to the host as a native call
        vm.Call(kit, "build", new ScriptReference(new ScriptObject(Array.Empty<ScriptClass>())));
        foreach (var file in _loader.LoadedFiles) UsedClassFiles.Add(file);

        return new SlrrKit(kit.Fields.TryGetValue("name", out var name) ? name.AsText : null, host.Parts);
    }

    /// <summary>
    /// A part as it is at one moment, to come back to: its fields and, through them, the elements of its arrays and
    /// the fields of the objects it holds. Those are shared by reference; putting back the part's fields alone
    /// would leave what a method did inside them.
    /// </summary>
    private sealed class State
    {
        private readonly Dictionary<ScriptObject, Dictionary<string, ScriptValue>> _objects = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<ScriptArray, Dictionary<int, ScriptValue>> _arrays = new(ReferenceEqualityComparer.Instance);

        public State(ScriptObject part)
        {
            Capture(new ScriptReference(part));
        }

        public void Restore()
        {
            foreach (var (instance, fields) in _objects)
            {
                instance.Fields.Clear();
                foreach (var (name, value) in fields) instance.Fields[name] = value;
            }

            foreach (var (array, items) in _arrays)
            {
                array.Items.Clear();
                foreach (var (index, value) in items) array.Items[index] = value;
            }
        }

        private void Capture(ScriptValue value)
        {
            switch (value)
            {
                case ScriptReference { Target: var instance } when !_objects.ContainsKey(instance):
                    _objects[instance] = new Dictionary<string, ScriptValue>(instance.Fields);
                    foreach (var field in instance.Fields.Values.ToList()) Capture(field);
                    break;

                case ScriptArray array when !_arrays.ContainsKey(array):
                    _arrays[array] = new Dictionary<int, ScriptValue>(array.Items);
                    foreach (var item in array.Items.Values.ToList()) Capture(item);
                    break;
            }
        }
    }

    private static Dictionary<string, object> Snapshot(ScriptObject part, HashSet<string> hidden)
    {
        var result = new Dictionary<string, object>();
        foreach (var (name, value) in part.Fields)
        {
            if (!hidden.Contains(name) && Export(value) is { } exported) result[name] = exported;
        }

        return result;
    }

    private static Dictionary<string, object> Changes(Dictionary<string, object> before, Dictionary<string, object> after) =>
        after.Where(p => !before.TryGetValue(p.Key, out var old) || !SameValue(old, p.Value)).ToDictionary(p => p.Key, p => p.Value);

    private static bool SameValue(object a, object b) =>
        a is List<object> listA && b is List<object> listB ? listA.SequenceEqual(listB) : a.Equals(b);

    private static object? Export(ScriptValue value)
    {
        switch (value)
        {
            case ScriptNumber number:
                return number.IsInteger ? (long)number.Amount : (object)Math.Round(number.Amount, 6);
            case ScriptText text:
                return text.Content;
            case ScriptResource resource:
                return $"{resource.Rpk}#0x{resource.Id:X4}";
            case ScriptArray array:
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

    /// <summary>Notes what a kit puts in the inventory</summary>
    private sealed class KitHost : ScriptHost
    {
        public List<(string Rpk, int TypeId)> Parts { get; } = new();

        public override ScriptValue? CallNative(ScriptVm vm, ScriptObject self, string method, ScriptValue[] arguments, bool uncertain)
        {
            if (method == "insertItem" && arguments.Length == 1 && arguments[0] is ScriptResource resource) Parts.Add((resource.Rpk, resource.Id));
            return null;
        }
    }

    /// <summary>No game around: parts on slots are unknown (but remember their slot), mounting stock parts is only noted</summary>
    private sealed class ExtractionHost : ScriptHost, IScriptInterceptor
    {
        public List<SlrrStockPart> StockParts { get; } = new();

        public ScriptValue? Intercept(ScriptVm vm, ScriptObject self, string method, ScriptValue[] arguments, bool uncertain)
        {
            if (method != "addPart" || arguments.Length < 2) return null;

            if (arguments[0] is ScriptResource resource)
                StockParts.Add(new SlrrStockPart(resource.Rpk, resource.Id, arguments[1].AsText, uncertain));
            return ScriptValue.Unknown;
        }

        public override ScriptValue? CallNative(ScriptVm vm, ScriptObject self, string method, ScriptValue[] arguments, bool uncertain)
        {
            switch (method)
            {
                case "setMaxWear" when arguments.Length == 1:
                    if (!uncertain && arguments[0] is ScriptNumber) self.Fields[MaxWearProperty] = arguments[0];
                    return ScriptValue.Unknown;

                case "partOnSlot" when arguments.Length == 1:
                    return arguments[0] is ScriptNumber { IsInteger: true } slot ? new ScriptUnknown((int)slot.Amount) : ScriptValue.Unknown;

                default:
                    return null;
            }
        }
    }
}
