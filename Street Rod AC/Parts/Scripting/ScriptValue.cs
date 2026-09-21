using System.Globalization;

namespace Street_Rod_AC.Parts.Scripting;

/// <summary>A value inside the script VM</summary>
public abstract record ScriptValue
{
    public static readonly ScriptValue Null = new ScriptNull();
    public static readonly ScriptValue Unknown = new ScriptUnknown();

    public static ScriptValue Of(double value) => new ScriptNumber(value, false);
    public static ScriptValue Of(int value) => new ScriptNumber(value, true);
    public static ScriptValue Of(bool value) => new ScriptNumber(value ? 1 : 0, true);
    public static ScriptValue Of(string? value) => value == null ? Null : new ScriptText(value);
    public static ScriptValue Of(ScriptObject? value) => value == null ? Null : new ScriptReference(value);

    /// <summary>The number behind the value, if it is one</summary>
    public double? AsNumber => this is ScriptNumber number ? number.Amount : null;

    public string? AsText => this is ScriptText text ? text.Content : null;
    public ScriptObject? AsObject => this is ScriptReference reference ? reference.Target : null;
}

public sealed record ScriptNumber(double Amount, bool IsInteger) : ScriptValue
{
    public override string ToString() => IsInteger
        ? ((long)Amount).ToString(CultureInfo.InvariantCulture)
        : Amount.ToString(CultureInfo.InvariantCulture);
}

public sealed record ScriptText(string Content) : ScriptValue;

/// <summary>A resource id as scripts write it: parts.engines.Mopar:0x0001r</summary>
public sealed record ScriptResource(string Rpk, int Id) : ScriptValue;

public sealed record ScriptNull : ScriptValue;

/// <summary>
/// Not knowable: the result of a native the host does not provide, or of anything computed from one.
/// Remembers the part slot it was read from, so that a check on it can be attributed to that slot.
/// </summary>
public sealed record ScriptUnknown(int? Slot = null) : ScriptValue;

public sealed record ScriptReference(ScriptObject Target) : ScriptValue;

/// <summary>A class used as a value: the left side of a static call or field</summary>
public sealed record ScriptClassReference(string ClassName) : ScriptValue;

public sealed record ScriptArray(int Length) : ScriptValue
{
    public SortedDictionary<int, ScriptValue> Items { get; } = new();
}

/// <summary>An instance of a script class: its class chain (own class first) and its fields, statics included</summary>
public sealed class ScriptObject
{
    public ScriptObject(IReadOnlyList<ScriptClass> chain)
    {
        Chain = chain;
    }

    public IReadOnlyList<ScriptClass> Chain { get; }
    public Dictionary<string, ScriptValue> Fields { get; } = new();

    /// <summary>Whatever the host wants to find again when the object comes back to it in a native call</summary>
    public object? Tag { get; set; }

    public string ClassName => Chain[0].ClassName ?? string.Empty;

    /// <summary>instanceof, by full class name</summary>
    public bool Is(string className) => Chain.Any(c => c.ClassName == className);

    public double Number(string field, double fallback = 0) =>
        Fields.TryGetValue(field, out var value) && value is ScriptNumber number ? number.Amount : fallback;

    public override string ToString() => ClassName;
}
