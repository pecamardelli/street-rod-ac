using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Parts.Cars;

/// <summary>
/// What parts cost. New price is the part script's own value; a used part is worth what the source game
/// gave for one: damage scales it, wear takes up to 70% off.
/// </summary>
public static class PartPricing
{
    /// <summary>Parts without a script (batteries, plates) carry no value of their own</summary>
    private const double ValueWithoutScript = 35.0;

    private const double WearFloor = 0.3;

    // More than anything in the game costs, well inside what a decimal holds
    private const double MaxPrice = 1e12;

    /// <summary>What a shop asks for a used part, against the same part new in the same shape</summary>
    public const double UsedShopFactor = 0.6;

    /// <summary>What a shop pays when it buys a part</summary>
    public const double TradeInFactor = 0.4;

    /// <summary>
    /// From the money of the part scripts to the money of the game. The scripts price parts in the dollars of
    /// the source game's day; the game's cars cost what cars cost in 1970.
    ///
    /// Process-wide on purpose: every price in the game is one of these, asked for from anywhere without a service
    /// at hand. The game sets it once, from its settings (<c>AppSettings.PartsPriceScale</c>), when the parts
    /// service is made; the tools that link this file (EngineBench) have no settings and price in the scripts'
    /// own dollars at 1. A value that is not a positive number (a hand-edited settings file) counts as 1.
    /// </summary>
    public static double Scale
    {
        get => _scale;
        set => _scale = double.IsFinite(value) && value > 0 ? value : 1.0;
    }

    private static double _scale = 1.0;

    public static double NewPrice(PartDefinition part)
    {
        // pack.json may hold "NaN", "Infinity" or 1e400 (which Newtonsoft reads as infinity): no price is the default one
        var value = part.Number("value");
        return (value > 0 && double.IsFinite(value) ? value : ValueWithoutScript) * Scale;
    }

    /// <summary>Worth of one part in its condition, nothing mounted on it counted</summary>
    public static double Worth(PartDefinition part, PartInstance instance) =>
        NewPrice(part) * Math.Clamp(instance.Tear, 0, 1) * (WearFloor + (1 - WearFloor) * Math.Clamp(instance.Wear, 0, 1));

    /// <summary>Worth of a part with everything that is mounted on it</summary>
    public static double WorthOfAssembly(PartsCatalog catalog, PartInstance root) =>
        root.SelfAndDescendants().Sum(p => catalog.Get(p.DefinitionId) is { } definition ? Worth(definition, p) : 0);

    public static decimal Round(double price)
    {
        // A decimal holds up to 7.9e28 and nothing that is not a number: the cast would throw
        price = double.IsFinite(price) ? Math.Clamp(price, 0, MaxPrice) : 0;
        return price < 20 ? Math.Max(1, Math.Round((decimal)price)) : Math.Round((decimal)price / 5) * 5;
    }
}
