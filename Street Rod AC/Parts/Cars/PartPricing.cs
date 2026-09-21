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

    /// <summary>What a shop asks for a used part, against the same part new in the same shape</summary>
    public const double UsedShopFactor = 0.6;

    /// <summary>What a shop pays when it buys a part</summary>
    public const double TradeInFactor = 0.4;

    /// <summary>
    /// From the money of the part scripts to the money of the game. The scripts price parts in the dollars of
    /// the source game's day; the game's cars cost what cars cost in 1970.
    /// </summary>
    public static double Scale { get; set; } = 1.0;

    public static double NewPrice(PartDefinition part)
    {
        var value = part.Number("value");
        return (value > 0 ? value : ValueWithoutScript) * Scale;
    }

    /// <summary>Worth of one part in its condition, nothing mounted on it counted</summary>
    public static double Worth(PartDefinition part, PartInstance instance) =>
        NewPrice(part) * Math.Clamp(instance.Tear, 0, 1) * (WearFloor + (1 - WearFloor) * Math.Clamp(instance.Wear, 0, 1));

    /// <summary>Worth of a part with everything that is mounted on it</summary>
    public static double WorthOfAssembly(PartsCatalog catalog, PartInstance root) =>
        root.SelfAndDescendants().Sum(p => catalog.Get(p.DefinitionId) is { } definition ? Worth(definition, p) : 0);

    public static decimal Round(double price) => price < 20 ? Math.Max(1, Math.Round((decimal)price)) : Math.Round((decimal)price / 5) * 5;
}
