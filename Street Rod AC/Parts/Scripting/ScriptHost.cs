using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Street_Rod_AC.Parts.Scripting;

/// <summary>
/// The world outside the scripts: everything the source game implemented natively.
/// Returning null means "not provided"; the VM then carries on with an unknown value.
/// </summary>
public interface IScriptHost
{
    /// <summary>A native (or missing) method called on a script object</summary>
    ScriptValue? CallNative(ScriptVm vm, ScriptObject self, string method, ScriptValue[] arguments, bool uncertain);

    /// <summary>A static method of a class that is not a script class, e.g. Float.toString or Math.sqrt</summary>
    ScriptValue? CallStatic(ScriptVm vm, string className, string method, ScriptValue[] arguments);
}

/// <summary>The few library classes part scripts lean on</summary>
public class ScriptHost : IScriptHost
{
    // %1.0f, %5.2f, %d as the scripts pass them to Float.toString
    private static readonly Regex Format = new(@"%[-+ 0#]*\d*(?:\.(?<precision>\d+))?(?<kind>[fdi])", RegexOptions.Compiled);

    // More digits than a double holds say nothing more
    private const int MaxPrecision = 20;

    public virtual ScriptValue? CallNative(ScriptVm vm, ScriptObject self, string method, ScriptValue[] arguments, bool uncertain) => null;

    public virtual ScriptValue? CallStatic(ScriptVm vm, string className, string method, ScriptValue[] arguments)
    {
        switch (className)
        {
            case "java.lang.Float" or "java.lang.Integer" when method == "toString" && arguments.Length >= 1:
                return arguments[0] is ScriptNumber number
                    ? ScriptValue.Of(arguments.Length > 1 && arguments[1] is ScriptText format ? Print(format.Content, number.Amount) : number.ToString())
                    : null;

            case "java.lang.Math" when arguments.All(a => a is ScriptNumber):
                var values = arguments.Select(a => ((ScriptNumber)a).Amount).ToArray();
                return (method, values.Length) switch
                {
                    ("sqrt", 1) => ScriptValue.Of(Math.Sqrt(values[0])),
                    ("abs", 1) => ScriptValue.Of(Math.Abs(values[0])),
                    ("sin", 1) => ScriptValue.Of(Math.Sin(values[0])),
                    ("cos", 1) => ScriptValue.Of(Math.Cos(values[0])),
                    ("pow", 2) => ScriptValue.Of(Math.Pow(values[0], values[1])),
                    ("min", 2) => ScriptValue.Of(Math.Min(values[0], values[1])),
                    ("max", 2) => ScriptValue.Of(Math.Max(values[0], values[1])),
                    _ => null
                };

            default:
                return null;
        }
    }

    private static string Print(string format, double value)
    {
        var result = new StringBuilder();
        var position = 0;
        foreach (Match match in Format.Matches(format))
        {
            result.Append(format, position, match.Index - position);
            // The precision is script text: "%.999999999f" would ask for a billion digits, more than ten overflow
            var precision = !match.Groups["precision"].Success ? 6
                : int.TryParse(match.Groups["precision"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var digits) ? Math.Clamp(digits, 0, MaxPrecision)
                : MaxPrecision;
            result.Append(match.Groups["kind"].Value == "f"
                ? value.ToString("F" + precision, CultureInfo.InvariantCulture)
                : Math.Truncate(value).ToString("F0", CultureInfo.InvariantCulture));
            position = match.Index + match.Length;
        }

        return result.Append(format, position, format.Length - position).Replace("%%", "%").ToString();
    }
}
