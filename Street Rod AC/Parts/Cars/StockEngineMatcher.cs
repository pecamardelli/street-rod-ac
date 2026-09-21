namespace Street_Rod_AC.Parts.Cars;

public sealed record EngineMatch(RatedBuild Build, double Score);

/// <summary>
/// Picks the engine build that best stands for the engine an Assetto Corsa car really has: same corporate
/// family first, then the words the names share (a 427 is a 427), then how close the power comes.
/// A suggestion, kept on the car's profile where it can be overruled.
/// </summary>
public static class StockEngineMatcher
{
    private const double SameFamily = 3.0;
    private const double OtherFamily = -3.0;
    private const double SharedNumber = 2.0;
    private const double SharedWord = 1.0;
    private const double PowerWeight = 4.0;
    private const double FactoryBuildBonus = 0.2;

    /// <param name="bhp">Power the car's own data claims, if any</param>
    public static List<EngineMatch> Rank(EngineBuildIndex index, string brand, string name, double? bhp)
    {
        var carName = $"{brand} {name}";
        var family = MakeFamilies.FamilyOf(carName);
        var tokens = MakeFamilies.Tokens(carName);

        return index.Runnable
            .Select(build => new EngineMatch(build, Score(build, family, tokens, bhp)))
            .OrderByDescending(m => m.Score)
            .ToList();
    }

    public static RatedBuild? Best(EngineBuildIndex index, string brand, string name, double? bhp) =>
        Rank(index, brand, name, bhp).FirstOrDefault()?.Build;

    private static double Score(RatedBuild build, string family, IReadOnlySet<string> tokens, double? bhp)
    {
        var score = 0.0;
        if (family.Length > 0 && build.Family.Length > 0) score += family == build.Family ? SameFamily : OtherFamily;

        foreach (var token in tokens)
        {
            if (build.NameTokens.Contains(token)) score += char.IsDigit(token[0]) ? SharedNumber : SharedWord;
        }

        if (bhp is > 0 && build.PowerHp > 0) score -= PowerWeight * Math.Abs(Math.Log(build.PowerHp / bhp.Value));
        if (build.Build.Origin == EngineBuild.OriginCar) score += FactoryBuildBonus;
        return score;
    }

    /// <summary>The number in a power figure as car data writes it: "430bhp", "335 hp", "250 cv"</summary>
    public static double? ParsePower(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var digits = new string(text.SkipWhile(c => !char.IsDigit(c)).TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        return double.TryParse(digits, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : null;
    }
}
