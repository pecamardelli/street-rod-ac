using System.Globalization;
using System.Text.RegularExpressions;

namespace Street_Rod_AC;

/// <summary>
/// A part that is left out, with the part that takes its place everywhere it was named or fitted; a set of
/// several identical parts is replaced by that many (a dual-quad set by two carburettors)
/// </summary>
internal sealed record MergeRule(Regex Pattern, string KeptId, int Count);

/// <summary>A model of several identical items in a row (a carburettor set) kept as one item; builds get that many</summary>
internal sealed record SingleRule(Regex Pattern, int Count, float Spacing);

/// <summary>
/// A pad that took a set of carburettors becomes one pad per carburettor, plus a slot over them for the air
/// cleaner that spans the set
/// </summary>
internal sealed record PadRule(Regex Pattern, int Slot, string Fitting, int Count, float Spacing, float[] AirOffset, string? AirFitting);

internal sealed record NameRule(Regex Pattern, string Name);

/// <summary>A standard fitting given to a slot of every part a pattern picks out</summary>
internal sealed record FitRule(Regex Pattern, int Slot, List<string> Fittings);

/// <summary>Parts that are drawn with another part's model (the same product modelled better in another pack)</summary>
internal sealed record ModelRule(Regex Pattern, string DonorId);

/// <summary>A slot moved in its part's space, to bring one pack's slot convention onto another's</summary>
internal sealed record ShiftRule(Regex Pattern, int Slot, float[] Offset);

/// <summary>
/// Where the parts of an rpk go: all of them (no selector), or those a selector picks out, by the name of a
/// script class they descend from, a category they are filed under, or their own name (* for anything)
/// </summary>
internal sealed record RenameRule(string Rpk, string? Selector, string PackId)
{
    /// <summary>The selector as a pattern, null for the rule that takes the rest of the rpk</summary>
    public Regex? Pattern { get; } = Selector == null ? null : Program.Pattern(Selector);
}

/// <summary>
/// The converter's command line. Every rule is checked as it is read: a rule the converter cannot follow stops the
/// run before anything is read from the install or written to the output.
/// </summary>
internal sealed class ConverterOptions
{
    public const string NotesOption = "--notes";
    public const string ReplaceOption = "--replace";
    public const string DropOption = "--drop";
    public const string RenameOption = "--rename";
    public const string MergeOption = "--merge";
    public const string FitOption = "--fit";
    public const string ModelOption = "--model";
    public const string ShiftOption = "--shift";
    public const string SingleOption = "--single";
    public const string PadsOption = "--pads";
    public const string NameOption = "--name";
    public const string ShiftsOption = "--shifts";
    public const string AbsorbOption = "--absorb";
    public const string PreviousOption = "--previous";
    public const string TwinOption = "--twin";
    public const string NoTwin = "-";
    public const string MeasureOption = "--measure";

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public string Slrr { get; private set; } = string.Empty;
    public string Output { get; private set; } = string.Empty;

    /// <summary>The one pack to convert, null for all of them</summary>
    public string? Filter { get; private set; }

    public string? Notes { get; private set; }
    public string? ShiftsFile { get; private set; }
    public string? Previous { get; private set; }
    public Dictionary<string, string?> TwinRules { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<Regex> Measure { get; } = new();
    public List<string> Absorb { get; } = new();
    public Dictionary<string, string> Replacements { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<RenameRule> Renames { get; } = new();
    public List<Regex> Drops { get; } = new();
    public List<MergeRule> Merges { get; } = new();
    public List<FitRule> Fits { get; } = new();
    public List<ModelRule> ModelRules { get; } = new();
    public List<ShiftRule> Shifts { get; } = new();
    public List<SingleRule> SingleRules { get; } = new();
    public List<PadRule> PadRules { get; } = new();
    public List<NameRule> NameRules { get; } = new();

    /// <summary>
    /// The options, or null when the command line is wrong; what is wrong has been printed then
    /// </summary>
    /// <remarks>
    /// --notes &lt;folder&gt;: text files with engine builds written down as stock_parts_list_E lines
    /// --replace &lt;old pack&gt;=&lt;new pack&gt;: the old pack stays out, what names its parts gets their twins of the new one
    /// --drop &lt;part id pattern&gt;: parts left out altogether (a pack's take on engines another pack does better)
    /// --rename &lt;rpk pack&gt;[:&lt;selector&gt;]=&lt;pack id&gt;: the pack, or the parts of it a selector picks out, go by a name
    ///   of our own (the mod's file name says nothing to a player, and one rpk may hold rims and tyres both)
    /// --merge &lt;part id pattern&gt;=&lt;part id&gt;: the parts are left out and the named part stands in for them: it is
    ///   what builds, saves and attach lines naming them get, and it fits wherever they fitted
    /// --fit &lt;part id pattern&gt;:&lt;slot&gt;=&lt;fitting&gt;[+&lt;fitting&gt;]: the slot mounts by a standard fitting, so it goes on
    ///   every slot that takes it, whatever the pack; the slots that take it are found from the attach lines
    ///   (a fitting written "takes:carb:4bbl" marks the slot as one that takes it instead)
    /// --model &lt;part id pattern&gt;=&lt;part id&gt;: the parts are drawn with the named part's model (and its slot
    ///   geometry), keeping their own scripts: the same product, modelled better in another pack
    /// --shift &lt;part id pattern&gt;:&lt;slot&gt;=&lt;dx&gt;/&lt;dy&gt;/&lt;dz&gt;: the slot moves in its part's space (metres), to bring
    ///   one pack's slot convention onto another's where parts of different packs meet by a fitting
    /// --single &lt;part id pattern&gt;=&lt;count&gt;@&lt;spacing&gt;: the model draws &lt;count&gt; identical items in a row (a set of
    ///   carburettors); one is kept, builds naming the part get &lt;count&gt; of it. A merge "=&lt;part id&gt;*&lt;count&gt;"
    ///   does the same for a set that another part stands in for
    /// --pads &lt;part id pattern&gt;:&lt;slot&gt;=&lt;fitting&gt;*&lt;count&gt;@&lt;spacing&gt;@&lt;dx&gt;/&lt;dy&gt;/&lt;dz&gt;[@&lt;air fitting&gt;]: a pad that took
    ///   a set becomes &lt;count&gt; pads taking &lt;fitting&gt;, plus a slot over them (at the offset) for an air cleaner
    ///   spanning the set
    /// --name &lt;part id pattern&gt;=&lt;display name&gt;: what the part is called once it is not what its script says
    /// --shifts &lt;file&gt;: slots nudged into place in the garage (slot_shifts.json), kept for good: what the game
    ///   wrote next to the packs since the last run is folded into this file first, then all of it is applied
    /// --absorb &lt;slot_shifts.json&gt;[;...]: more of the game's files to fold in (the game writes next to the content
    ///   it runs on, in a build folder) and take away; the option may be given once per file (a path may hold a comma)
    /// --previous &lt;folder&gt;: an earlier conversion (the content in use): a part it had that goes by another name
    ///   now, because a release of the mod renamed its files, is aliased to what its rpk resource is now, and
    ///   its older aliases are kept while their targets exist. Saves made with it keep working
    /// --twin &lt;old part id&gt;=&lt;new part id&gt;: a pair of a replaced pack written by hand, where the matcher pairs
    ///   wrongly (a DOHC camshaft has no look-alike among pushrod parts); "-" for a part the new release does without
    /// --measure &lt;part id pattern&gt;,...: convert nothing, print the slots of the parts and the bounds of their meshes
    ///   (metres, the model's own space), to see by what convention a pack places a joint before parts of two
    ///   packs are made to meet by a fitting: a carburettor slot 6 cm above the base sinks that far into a pad
    ///   placed at the flange
    /// </remarks>
    public static ConverterOptions? Parse(string[] args)
    {
        string? notes = null;
        string? shiftsFile = null;
        string? previous = null;
        var options = new ConverterOptions();
        var positional = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == NotesOption && i + 1 < args.Length) notes = args[++i];
            else if (args[i] == ShiftsOption && i + 1 < args.Length) shiftsFile = args[++i];
            else if (args[i] == PreviousOption && i + 1 < args.Length) previous = args[++i];
            else if (args[i] == MeasureOption && i + 1 < args.Length) options.Measure.AddRange(args[++i].Split(',').Select(p => Program.Pattern(p.Trim())));
            else if (args[i] == TwinOption && i + 1 < args.Length)
            {
                foreach (var pair in args[++i].Split(',').Select(p => p.Split('=')))
                {
                    if (pair.Length != 2)
                    {
                        Console.WriteLine($"{TwinOption} takes <old part id>=<new part id> (or {NoTwin} for no twin), e.g. engines/fordi6_data/Ford_221_SP_cylinder_head=engines/ford_six/sprint_cylinder_head");
                        return null;
                    }

                    options.TwinRules[pair[0].Trim()] = pair[1].Trim() == NoTwin ? null : pair[1].Trim();
                }
            }
            // One file per option, or several joined by ';' (a path may hold a comma, hardly ever a semicolon)
            else if (args[i] == AbsorbOption && i + 1 < args.Length) options.Absorb.AddRange(args[++i].Split(';').Select(f => f.Trim()).Where(f => f.Length > 0));
            else if (args[i] == DropOption && i + 1 < args.Length)
            {
                options.Drops.AddRange(args[++i].Split(',').Select(p => Program.Pattern(p.Trim())));
            }
            else if (args[i] == MergeOption && i + 1 < args.Length)
            {
                foreach (var pair in args[++i].Split(',').Select(p => p.Split('=')))
                {
                    var kept = pair.Length == 2 ? pair[1].Trim().Split('*') : Array.Empty<string>();
                    var count = 1;
                    if (kept.Length is < 1 or > 2 || (kept.Length == 2 && !int.TryParse(kept[1], out count)))
                    {
                        Console.WriteLine($"{MergeOption} takes <part id pattern>=<part id>[*<count>], e.g. engines/gm/stock_2x4brl_carburator=engines/gm/stock_4brl_carburator*2");
                        return null;
                    }

                    options.Merges.Add(new MergeRule(Program.Pattern(pair[0].Trim()), kept[0], count));
                }
            }
            else if (args[i] == SingleOption && i + 1 < args.Length)
            {
                foreach (var rule in args[++i].Split(','))
                {
                    var pair = rule.Split('=', 2);
                    var value = pair.Length == 2 ? pair[1].Split('@') : Array.Empty<string>();
                    // A row is two items or more, some distance apart: a spacing of 0 would divide by zero when the
                    // model is sliced, and a count below 2 leaves nothing to keep one of
                    if (value.Length != 2 || !int.TryParse(value[0], out var count) || !TryFloat(value[1], out var spacing)
                        || count < 2 || !(spacing > 0))
                    {
                        Console.WriteLine($"{SingleOption} takes <part id pattern>=<count>@<spacing>, a count of 2 or more and a spacing above 0, " +
                                          "e.g. engines/generic/Carburetors_2x4BRL_King_Demon=2@0.22");
                        return null;
                    }

                    options.SingleRules.Add(new SingleRule(Program.Pattern(pair[0].Trim()), count, spacing));
                }
            }
            else if (args[i] == PadsOption && i + 1 < args.Length)
            {
                foreach (var rule in args[++i].Split(','))
                {
                    var isRule = TrySlotRule(rule, out var pattern, out var slot, out var text);
                    var value = isRule ? text.Split('@') : Array.Empty<string>();
                    var fitting = value.Length > 0 ? value[0].Split('*') : Array.Empty<string>();
                    float[]? offset = null;
                    // As many pads as carburettors, two or more, some distance apart (see --single)
                    if (!isRule || value.Length is < 3 or > 4 || fitting.Length != 2
                        || !int.TryParse(fitting[1], out var count) || !TryFloat(value[1], out var spacing)
                        || !TryVector(value[2], out offset) || count < 2 || !(spacing > 0))
                    {
                        Console.WriteLine($"{PadsOption} takes <part id pattern>:<slot>=<fitting>*<count>@<spacing>@<dx>/<dy>/<dz>[@<air fitting>], " +
                                          "a count of 2 or more and a spacing above 0, e.g. engines/chrysler/dualquad_intake:7=carb:4bbl*2@0.22@-0.043/0.108/-0.04@air:2x4");
                        return null;
                    }

                    options.PadRules.Add(new PadRule(Program.Pattern(pattern), slot, fitting[0], count, spacing, offset!, value.Length > 3 ? value[3] : null));
                }
            }
            else if (args[i] == NameOption && i + 1 < args.Length)
            {
                foreach (var pair in args[++i].Split(',').Select(p => p.Split('=', 2)))
                {
                    if (pair.Length != 2)
                    {
                        Console.WriteLine($"{NameOption} takes <part id pattern>=<display name>");
                        return null;
                    }

                    options.NameRules.Add(new NameRule(Program.Pattern(pair[0].Trim()), pair[1].Trim()));
                }
            }
            else if (args[i] == ModelOption && i + 1 < args.Length)
            {
                foreach (var pair in args[++i].Split(',').Select(p => p.Split('=')))
                {
                    if (pair.Length != 2)
                    {
                        Console.WriteLine($"{ModelOption} takes <part id pattern>=<part id>, e.g. engines/generic/Holley_4brl_carburator=engines/generic/Carburetors_4BRL_street_HOLLEY");
                        return null;
                    }

                    options.ModelRules.Add(new ModelRule(Program.Pattern(pair[0].Trim()), pair[1].Trim()));
                }
            }
            else if (args[i] == ShiftOption && i + 1 < args.Length)
            {
                foreach (var rule in args[++i].Split(','))
                {
                    if (!TrySlotRule(rule, out var pattern, out var slot, out var text) || !TryVector(text, out var offset))
                    {
                        Console.WriteLine($"{ShiftOption} takes <part id pattern>:<slot>=<dx>/<dy>/<dz> in metres, e.g. engines/chrysler/Intake_manifold_*:7=0/-0.062/0");
                        return null;
                    }

                    options.Shifts.Add(new ShiftRule(Program.Pattern(pattern), slot, offset));
                }
            }
            else if (args[i] == FitOption && i + 1 < args.Length)
            {
                foreach (var rule in args[++i].Split(','))
                {
                    if (!TrySlotRule(rule, out var pattern, out var slot, out var text))
                    {
                        Console.WriteLine($"{FitOption} takes <part id pattern>:<slot>=<fitting>[+<fitting>], e.g. engines/generic/Carburetors_4BRL_*:10=carb:4bbl");
                        return null;
                    }

                    options.Fits.Add(new FitRule(Program.Pattern(pattern), slot, text.Split('+').Select(f => f.Trim()).ToList()));
                }
            }
            else if (args[i] == ReplaceOption && i + 1 < args.Length)
            {
                foreach (var pair in args[++i].Split(',').Select(p => p.Split('=')))
                {
                    if (pair.Length != 2)
                    {
                        Console.WriteLine($"{ReplaceOption} takes <old pack>=<new pack>, e.g. engines/Mopar=engines/chrysler");
                        return null;
                    }

                    options.Replacements[pair[0].Trim()] = pair[1].Trim();
                }
            }
            else if (args[i] == RenameOption && i + 1 < args.Length)
            {
                foreach (var pair in args[++i].Split(',').Select(p => p.Split('=')))
                {
                    var source = pair.Length == 2 ? pair[0].Trim().Split(':') : Array.Empty<string>();
                    if (source.Length is < 1 or > 2)
                    {
                        Console.WriteLine($"{RenameOption} takes <rpk pack>[:<class, category or part name>]=<pack id>, " +
                                          "e.g. engines/Chrysler_V8_pak=engines/chrysler or wheels:Tyre=tyres/sl_tuners");
                        return null;
                    }

                    var selector = source.Length == 2 ? source[1].Trim() : null;
                    options.Renames.Add(new RenameRule(source[0].Trim(), selector, pair[1].Trim()));
                }
            }
            else if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                // An option nobody knows, or one whose value is missing (a shell drops an empty argument): taken as
                // the pack filter it would convert nothing and say so only in the totals
                Console.WriteLine($"Unknown option or missing value: {args[i]}");
                positional.Clear();
                break;
            }
            else positional.Add(args[i]);
        }

        if (positional.Count < 2)
        {
            Console.WriteLine("Usage: SlrrPartsConverter <SLRR folder> <output folder> [pack filter] [--notes <folder>] " +
                              "[--replace <old pack>=<new pack>] [--drop <part id pattern>,...] [--rename <rpk pack>[:<selector>]=<pack id>,...] " +
                              "[--merge <part id pattern>=<part id>,...] [--fit <part id pattern>:<slot>=<fitting>[+<fitting>],...] " +
                              "[--model <part id pattern>=<part id>,...] [--shift <part id pattern>:<slot>=<dx>/<dy>/<dz>,...] " +
                              "[--single <part id pattern>=<count>@<spacing>,...] [--pads <part id pattern>:<slot>=<fitting>*<count>@<spacing>@<dx>/<dy>/<dz>[@<air fitting>],...] " +
                              "[--name <part id pattern>=<display name>,...] [--shifts <slot_shifts.json kept for good>] [--absorb <slot_shifts.json written by the game>[;...]]... " +
                              "[--previous <earlier conversion>] [--twin <old part id>=<new part id or ->,...] [--measure <part id pattern>,...]");
            Console.WriteLine(@"  e.g. SlrrPartsConverter ""D:\Games\SLRR"" ""Street Rod AC\Assets\Parts"" engines/Mopar  (the full run: tools\convert-parts.ps1)");
            return null;
        }

        // Build notes that are not there would leave engine_builds.json without every notes build, and nothing
        // would say so (a drive that is not mounted)
        if (notes != null && !Directory.Exists(notes))
        {
            Console.WriteLine($"{NotesOption} names a folder that is not there: {notes}");
            return null;
        }

        options.Slrr = positional[0];
        options.Output = positional[1];
        options.Filter = positional.Count > 2 ? positional[2] : null;
        options.Notes = notes;
        options.ShiftsFile = shiftsFile;
        options.Previous = previous;
        return options;
    }

    /// <summary>"&lt;part id pattern&gt;:&lt;slot&gt;=&lt;value&gt;", the shape of --fit, --shift and --pads rules</summary>
    private static bool TrySlotRule(string rule, out string pattern, out int slot, out string value)
    {
        var pair = rule.Split('=', 2);
        var colon = pair[0].LastIndexOf(':');
        pattern = colon < 0 ? string.Empty : pair[0][..colon].Trim();
        value = pair.Length == 2 ? pair[1] : string.Empty;
        slot = 0;
        return pair.Length == 2 && colon >= 0 && int.TryParse(pair[0][(colon + 1)..], out slot);
    }

    /// <summary>"&lt;dx&gt;/&lt;dy&gt;/&lt;dz&gt;" in metres</summary>
    private static bool TryVector(string text, out float[] vector)
    {
        var parts = text.Split('/');
        vector = new float[3];
        if (parts.Length != 3) return false;

        for (var axis = 0; axis < 3; axis++)
        {
            if (!TryFloat(parts[axis], out vector[axis])) return false;
        }

        return true;
    }

    private static bool TryFloat(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, Culture, out value);
}
