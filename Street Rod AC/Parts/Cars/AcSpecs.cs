using System.Globalization;
using System.Text.RegularExpressions;

namespace Street_Rod_AC.Parts.Cars;

/// <summary>
/// The numbers in a car's ui_car.json specs, as car makers and modders write them: "430bhp", "335 hp",
/// "350hp @ 6000rpm", "250 cv", "1,250kg", "1.250 kg", "147,5 bhp". The one parser for them: every reader of
/// power or weight goes through here, so a car is the same car to the market, the diner and the engine matcher.
///
/// The first number in the text is the figure; what follows it ("@ 6000rpm") is not. Culture-free.
/// </summary>
public static partial class AcSpecs
{
    [GeneratedRegex(@"\d+(?:[.,]\d+)*")]
    private static partial Regex Number();

    /// <summary>Power as written, in whatever unit the text uses (bhp, hp or cv: they are within a few percent)</summary>
    public static bool TryParsePower(string? text, out double bhp) => TryParse(text, weight: false, out bhp);

    /// <summary>Weight in kg</summary>
    public static bool TryParseWeight(string? text, out double kg) => TryParse(text, weight: true, out kg);

    public static double? ParsePower(string? text) => TryParsePower(text, out var value) ? value : null;

    public static double? ParseWeight(string? text) => TryParseWeight(text, out var value) ? value : null;

    private static bool TryParse(string? text, bool weight, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var match = Number().Match(text);
        if (!match.Success) return false;

        var number = Normalize(match.Value, weight);
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) return false;
        if (!double.IsFinite(parsed) || parsed <= 0) return false;

        value = parsed;
        return true;
    }

    /// <summary>
    /// "1,250" and "1,250.5" are thousands with a comma; "147,5" is a decimal comma; "1.250" is a decimal point
    /// for power but thousands for a weight (no car weighs 1.25 kg)
    /// </summary>
    private static string Normalize(string number, bool weight)
    {
        var commas = number.Count(c => c == ',');
        var dots = number.Count(c => c == '.');

        if (commas > 0 && dots > 0)
            return number.IndexOf(',') < number.IndexOf('.') ? number.Replace(",", "") : number.Replace(".", "").Replace(',', '.');

        if (commas > 0)
            return IsThousands(number, ',') ? number.Replace(",", "") : commas == 1 ? number.Replace(',', '.') : number.Replace(",", "");

        if (dots > 1 || (dots == 1 && weight && IsThousands(number, '.')))
            return number.Replace(".", "");

        return number;
    }

    /// <summary>1-3 digits, then groups of exactly three after each separator</summary>
    private static bool IsThousands(string number, char separator)
    {
        var groups = number.Split(separator);
        return groups[0].Length is >= 1 and <= 3 && groups.Skip(1).All(g => g.Length == 3);
    }
}
