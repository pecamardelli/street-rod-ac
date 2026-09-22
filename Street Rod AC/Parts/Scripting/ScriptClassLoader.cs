using System.IO;

namespace Street_Rod_AC.Parts.Scripting;

/// <summary>
/// Finds compiled script classes under a root folder laid out like the source game.
/// java.game.parts.engines.Mopar.X lives in parts\engines\Mopar\scripts\X.class, but the shared classes of
/// java.game.parts.enginepart.block live in parts\scripts\enginepart\block: the scripts folder may sit at any
/// level of the package path. Cars share the package java.game.cars while each keeps its classes in its own
/// folder, so the folder of the referring class is tried first.
/// A loaded class never changes, so one loader serves any number of VMs, on any thread.
/// </summary>
public sealed class ScriptClassLoader
{
    private const string ClassPrefix = "java.game.";
    private const string ScriptsFolder = "scripts";
    private const int MaxChain = 32;

    /// <summary>
    /// Classes mods extend that the game never shipped, made from the one their author copied: what to copy,
    /// what it extends instead, and the methods left out. Block_Inline_OHV (the Ford six's blocks) is
    /// Block_Vee_OHV with one cylinder head; its author's source sits among the SLRR notes, never compiled.
    /// </summary>
    private static readonly Dictionary<string, (string CopiedFrom, string BaseClass, string[] DroppedMethods)> StandIns =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["java.game.parts.enginepart.block.block_inline.Block_Inline_OHV"] =
                ("java.game.parts.enginepart.block.block_vee.Block_Vee_OHV", "java.game.parts.enginepart.block.Block_Inline",
                    new[] { "getLeftCylinderHead", "getRightCylinderHead" })
        };

    private readonly Dictionary<string, ScriptClass?> _classes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public ScriptClassLoader(string root)
    {
        Root = root;
    }

    public string Root { get; }

    /// <summary>Every class file read so far</summary>
    public IEnumerable<string> LoadedFiles
    {
        get { lock (_lock) return _files.ToList(); }
    }

    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);

    public ScriptClass? Load(string file)
    {
        lock (_lock)
        {
            if (_classes.TryGetValue(file, out var cached)) return cached;

            var loaded = File.Exists(file) ? ScriptClass.Load(file) : null;
            if (loaded != null) _files.Add(Path.GetFullPath(file));
            return _classes[file] = loaded;
        }
    }

    public ScriptClass? Find(string className, string? nearFolder = null)
    {
        lock (_lock) return FindLocked(className, nearFolder);
    }

    private ScriptClass? FindLocked(string className, string? nearFolder)
    {
        if (nearFolder != null)
        {
            var sibling = Load(Path.Combine(nearFolder, className[(className.LastIndexOf('.') + 1)..] + ".class"));
            if (sibling?.ClassName == className) return sibling;
        }

        if (_classes.TryGetValue(className, out var cached)) return cached;

        ScriptClass? result = null;
        if (className.StartsWith(ClassPrefix, StringComparison.Ordinal))
        {
            var segments = className[ClassPrefix.Length..].Split('.');
            var package = segments[..^1];
            var file = segments[^1] + ".class";

            for (var level = package.Length; level >= 0 && result == null; level--)
            {
                result = Load(Path.Combine(new[] { Root }
                    .Concat(package[..level]).Append(ScriptsFolder).Concat(package[level..]).Append(file).ToArray()));
            }
        }

        if (result == null && StandIns.TryGetValue(className, out var standIn) && FindLocked(standIn.CopiedFrom, null) is { } original)
            result = original.DerivedAs(className, standIn.BaseClass, standIn.DroppedMethods);

        return _classes[className] = result;
    }

    /// <summary>The class and everything it extends, own class first</summary>
    public List<ScriptClass> Chain(ScriptClass type)
    {
        var chain = new List<ScriptClass> { type };
        for (var current = type; chain.Count < MaxChain;)
        {
            var parent = current.BaseClass == null ? null : Find(current.BaseClass, current.Folder);
            if (parent == null || chain.Contains(parent)) break;

            chain.Add(parent);
            current = parent;
        }

        return chain;
    }

    public List<ScriptClass>? Chain(string className, string? nearFolder = null) =>
        Find(className, nearFolder) is { } type ? Chain(type) : null;
}
