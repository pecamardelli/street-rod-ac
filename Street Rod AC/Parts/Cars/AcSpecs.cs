using System.Globalization;
using System.Text.RegularExpressions;

namespace Street_Rod_AC.Parts.Cars;

/// <summary>
/// The numbers in a car's ui_car.json specs, as car makers and modders write them: "430bhp", "335 hp",
/// "350hp @ 6000rpm", "250 cv", "150kW", "1,250kg", "1.250 kg", "2755 lbs", "147,5 bhp"; and a track's length in its
/// ui_track.json: "1,000 m", "1.2 km", "0.25 mi", "1320 ft". The one parser for them: every reader of power, weight
/// or length goes through here, so a car is the same car to the market, the diner and the engine matcher.
///
/// The first number in the text is the figure, and the unit is the word right after it (spaces allowed): what comes
/// later ("@ 6000rpm") is neither. Power comes back in hp and weight in kg:
/// - power: kW is converted (×1.34102). bhp, hp, whp and the metric horsepowers (ps, cv, ch, pk) are taken as they
///   are: metric hp is 0.986 hp, closer than the spread between the ways modders measure power, and the catalog has
///   always priced a "250 cv" car as a 250 hp one;
/// - weight: lb/lbs/pounds are converted (×0.45359237) and tonnes (t) ×1000. kg is as is;
/// - length: km (×1000), mi (×1609.344) and ft (×0.3048) are converted. m is as is.
/// No unit, or one that is not known, leaves the figure as it is. Culture-free.
/// </summary>
public static partial class AcSpecs
{
    private const double HpPerKw = 1.34102;
    private const double KgPerLb = 0.45359237;
    private const double MetresPerMile = 1609.344;
    private const double MetresPerFoot = 0.3048;

    private enum Figure { Power, Weight, Length }

    [GeneratedRegex(@"\d+(?:[.,]\d+)*")]
    private static partial Regex Number();

    /// <summary>The letters right after the number, if the next thing after it (spaces aside) is a word</summary>
    [GeneratedRegex(@"\G\s*([A-Za-z]+)")]
    private static partial Regex Unit();

    /// <summary>Power in hp: kW converted, bhp/hp/cv/ps taken as they are</summary>
    public static bool TryParsePower(string? text, out double bhp) => TryParse(text, Figure.Power, out bhp);

    /// <summary>Weight in kg: lb and tonnes converted</summary>
    public static bool TryParseWeight(string? text, out double kg) => TryParse(text, Figure.Weight, out kg);

    public static double? ParsePower(string? text) => TryParsePower(text, out var value) ? value : null;

    public static double? ParseWeight(string? text) => TryParseWeight(text, out var value) ? value : null;

    /// <summary>A track's length in metres: km, mi and ft converted; null when it is not a length</summary>
    public static double? ParseLength(string? text) => TryParse(text, Figure.Length, out var value) ? value : null;

    private static bool TryParse(string? text, Figure figure, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var match = Number().Match(text);
        if (!match.Success) return false;

        var unitMatch = Unit().Match(text, match.Index + match.Length);
        var unit = unitMatch.Success ? unitMatch.Groups[1].Value.ToLowerInvariant() : "";
        // "1.25 t" and "1.2 km" are a decimal point whatever a weight in kg or a length in m would make of it
        var large = figure switch
        {
            Figure.Weight => unit is not ("t" or "ton" or "tons" or "tonne" or "tonnes"),
            Figure.Length => unit is not ("km" or "mi" or "mile" or "miles"),
            _ => false
        };
        var number = Normalize(match.Value, large);
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) return false;
        if (!double.IsFinite(parsed) || parsed <= 0) return false;

        value = parsed * Factor(unit, figure);
        return true;
    }

    /// <summary>What turns the figure into hp, kg or m</summary>
    private static double Factor(string unit, Figure figure) => figure switch
    {
        Figure.Weight => unit switch
        {
            "lb" or "lbs" or "pound" or "pounds" => KgPerLb,
            "t" or "ton" or "tons" or "tonne" or "tonnes" => 1000,
            _ => 1
        },
        Figure.Length => unit switch
        {
            "km" => 1000,
            "mi" or "mile" or "miles" => MetresPerMile,
            "ft" or "feet" or "foot" => MetresPerFoot,
            _ => 1
        },
        _ => unit switch
        {
            "kw" => HpPerKw,
            _ => 1
        }
    };

    /// <summary>
    /// "1,250" and "1,250.5" are thousands with a comma; "147,5" is a decimal comma; "1.250" is a decimal point
    /// for power but thousands for a large figure, a weight in kg or a length in m (no car weighs 1.25 kg, no strip
    /// runs 1.25 m)
    /// </summary>
    private static string Normalize(string number, bool large)
    {
        var commas = number.Count(c => c == ',');
        var dots = number.Count(c => c == '.');

        if (commas > 0 && dots > 0)
            return number.IndexOf(',') < number.IndexOf('.') ? number.Replace(",", "") : number.Replace(".", "").Replace(',', '.');

        if (commas > 0)
            return IsThousands(number, ',') ? number.Replace(",", "") : commas == 1 ? number.Replace(',', '.') : number.Replace(",", "");

        if (dots > 1 || (dots == 1 && large && IsThousands(number, '.')))
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
