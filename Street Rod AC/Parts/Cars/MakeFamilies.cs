namespace Street_Rod_AC.Parts.Cars;

/// <summary>
/// Which corporate family a car or an engine belongs to, from the words of its name. Engines stay in the
/// family: a Pontiac may turn up with a Chevrolet V8, never with a Hemi.
/// </summary>
public static class MakeFamilies
{
    public const string GM = "gm";
    public const string Ford = "ford";
    public const string Mopar = "mopar";

    private static readonly Dictionary<string, string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gm"] = GM, ["chevrolet"] = GM, ["chevy"] = GM, ["cevrolet"] = GM, ["pontiac"] = GM, ["buick"] = GM, ["cadillac"] = GM,
        ["oldsmobile"] = GM, ["holden"] = GM, ["camaro"] = GM, ["corvette"] = GM, ["vette"] = GM, ["gto"] = GM, ["chevelle"] = GM,
        ["impala"] = GM, ["nova"] = GM,
        ["ford"] = Ford, ["shelby"] = Ford, ["mercury"] = Ford, ["lincoln"] = Ford, ["mustang"] = Ford, ["cobra"] = Ford, ["falcon"] = Ford,
        ["mopar"] = Mopar, ["dodge"] = Mopar, ["plymouth"] = Mopar, ["chrysler"] = Mopar, ["valiant"] = Mopar, ["desoto"] = Mopar,
        ["charger"] = Mopar, ["challenger"] = Mopar, ["barracuda"] = Mopar, ["cuda"] = Mopar, ["hemi"] = Mopar,
        ["volvo"] = "volvo", ["amc"] = "amc", ["jaguar"] = "jaguar", ["peugeot"] = "peugeot", ["hudson"] = "hudson",
        ["delorean"] = "delorean", ["packard"] = "packard"
    };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vette"] = "corvette", ["chevy"] = "chevrolet", ["cevrolet"] = "chevrolet", ["stang"] = "mustang", ["cuda"] = "barracuda"
    };

    // Words that say nothing about which car or engine it is
    private static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "top", "vinyl", "vinil", "custom", "stock", "coupe", "sport", "edition", "version", "race", "street"
    };

    /// <summary>Family of whatever the text names, "" when no word gives it away. The first word that tells wins.</summary>
    public static string FamilyOf(string text)
    {
        foreach (var token in Split(text))
        {
            if (Keywords.TryGetValue(token, out var family)) return family;
        }

        return string.Empty;
    }

    /// <summary>Lower-case words and numbers of a name that are worth comparing</summary>
    public static IReadOnlySet<string> Tokens(string text)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in Split(text))
        {
            if (Noise.Contains(token)) continue;

            // Model years come as '69 or 1969; displacements are three digits and stay
            var isNumber = token.All(char.IsDigit);
            if (isNumber ? token.Length == 3 : token.Length >= 3) tokens.Add(Aliases.GetValueOrDefault(token, token).ToLowerInvariant());
        }

        return tokens;
    }

    private static IEnumerable<string> Split(string text) =>
        text.Split(new[] { ' ', '-', '_', '/', '\'', '(', ')', ',', '.', '´' }, StringSplitOptions.RemoveEmptyEntries);
}
