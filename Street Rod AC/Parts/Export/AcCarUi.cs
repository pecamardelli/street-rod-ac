using System.IO;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Street_Rod_AC.Parts.Export;

/// <summary>
/// A car's ui/ui_car.json, read the one way every reader in the game and the tools reads it. Mods write it
/// loosely: comments, trailing commas, a year as 1969, 1969.0 or "1969", tags as a list or a single string.
/// This reads all of those and hands back what could be read; a file that is not JSON at all is an error
/// the caller reports, never an exception.
/// </summary>
public static class AcCarUi
{
    public static string PathIn(string carFolder) => Path.Combine(carFolder, "ui", "ui_car.json");

    /// <summary>The parsed file, or null with the reason (missing, unreadable, not an object)</summary>
    public static JObject? TryRead(string carFolder, out string? error)
    {
        var path = PathIn(carFolder);
        if (!File.Exists(path))
        {
            error = "no ui/ui_car.json";
            return null;
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return null;
        }

        return TryParse(text, out error);
    }

    public static JObject? TryParse(string json, out string? error)
    {
        try
        {
            using var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 64 };
            var token = JToken.ReadFrom(reader, new JsonLoadSettings
            {
                CommentHandling = CommentHandling.Ignore,
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Replace
            });

            error = token is JObject ? null : "ui_car.json is not a JSON object";
            return token as JObject;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>A string property; a number or bool is given as its invariant text</summary>
    public static string? GetString(JToken? parent, string name) =>
        (parent as JObject)?[name] switch
        {
            JValue { Type: JTokenType.String } v => (string?)v.Value,
            JValue { Type: JTokenType.Integer or JTokenType.Float or JTokenType.Boolean } v =>
                Convert.ToString(v.Value, CultureInfo.InvariantCulture),
            _ => null
        };

    /// <summary>An integer property written as 1969, 1969.0 or "1969"; null when it is none of those or out of range</summary>
    public static int? GetInt(JToken? parent, string name)
    {
        switch ((parent as JObject)?[name])
        {
            case JValue { Type: JTokenType.Integer } v when v.Value is long l && l is >= int.MinValue and <= int.MaxValue:
                return (int)l;
            case JValue { Type: JTokenType.Float } v when v.Value is double d && double.IsFinite(d) && d == Math.Floor(d) && d is >= int.MinValue and <= int.MaxValue:
                return (int)d;
            case JValue { Type: JTokenType.String } v when int.TryParse((string?)v.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                return parsed;
            default:
                return null;
        }
    }

    /// <summary>A list of strings written as an array (non-strings skipped) or as one string; empty when absent</summary>
    public static IReadOnlyList<string> GetStrings(JToken? parent, string name) =>
        (parent as JObject)?[name] switch
        {
            JArray array => array.OfType<JValue>().Where(v => v.Type == JTokenType.String)
                .Select(v => (string?)v.Value).Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList(),
            JValue { Type: JTokenType.String } v when !string.IsNullOrEmpty((string?)v.Value) => [(string)v.Value!],
            _ => []
        };

    /// <summary>A nested object ("specs"), or null</summary>
    public static JObject? GetObject(JToken? parent, string name) => (parent as JObject)?[name] as JObject;
}
