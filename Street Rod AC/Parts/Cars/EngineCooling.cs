using System.Text.RegularExpressions;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;

namespace Street_Rod_AC.Parts.Cars;

/// <summary>
/// How well a car's engine is cooled and oiled, for the race mode's heat and oil (race.ini <c>CAR_n_COOLING</c>): what
/// its radiator, water pump and fan carry off against what the engine makes, and how long its oil pan keeps the pickup
/// in oil.
///
/// A car's cooling was sized for its engine as it left the factory: the car's factory power, or the factory power of
/// the block in it now when that is more (a swap brings its own radiator), taken from the least powerful factory
/// build on that block. Tuning past it is what makes an engine run hot. The source game's radiators, fans and water
/// pumps are plain parts with no figures, so they count by kind: a performance radiator (aluminium, racing, one with an
/// electric fan) carries off a third more, a performance water pump a tenth more, and a car whose factory build had a
/// radiator has next to no cooling without one. The fan decides how much of the work is done standing still. A stock
/// oil pan holds its oil to 1.05 g, a deep or race pan to 1.25 g (1.3 with 6.5 litres or more): the cars pull 1 to 1.4
/// g in corners (Black Cat County, 2026-09-26), so a stock pan runs short only in a long sweeper at the limit, and
/// stickier tyres ask for a deeper pan.
///
/// Linked into EngineBench: works from parts, never from a <c>Car</c>.
/// </summary>
public sealed class EngineCooling
{
    public enum Kind { Other, Radiator, Fan, WaterPump }

    /// <summary>A performance radiator carries off this much more than a factory one</summary>
    public const double PerformanceRadiator = 1.35;

    /// <summary>A performance water pump moves this much more coolant</summary>
    public const double PerformancePump = 1.1;

    /// <summary>What an engine without its radiator is left to cool it with, of what it makes</summary>
    public const double NoRadiator = 0.15;

    public const double NoFan = 0.15;
    public const double StockFan = 0.35;
    public const double PerformanceFan = 0.5;

    /// <summary>A radiator with an electric fan of its own keeps up best standing still</summary>
    public const double ElectricFan = 0.55;

    public const double StockSumpG = 1.05;
    public const double RaceSumpG = 1.25;

    /// <summary>A race pan this big holds its oil a little longer</summary>
    public const double BigSumpLitres = 6.5;
    public const double BigSumpG = 1.3;

    /// <summary>No oil pan at all: the pickup is in the air under any g</summary>
    public const double NoSumpG = 0.5;

    public const string OilPansGroup = "Oil pans";

    private const RegexOptions Words_ = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly Regex RadiatorWord = new(@"radiator", Words_);
    private static readonly Regex FanWord = new(@"(^|[^a-z])fan($|[^a-z])", Words_);
    private static readonly Regex WaterPumpWords = new(@"water.*p[ou]mp", Words_);
    private static readonly Regex PerformanceRadiatorWords = new(@"alumin|racing|race|champion|be cool|electric fan", Words_);
    private static readonly Regex ElectricFanWords = new(@"electric fan", Words_);
    private static readonly Regex PerformancePumpWords = new(@"edelbrock|weiand|summit|high-flow|meziere|electric|march|paxton", Words_);
    private static readonly Regex PerformanceFanWords = new(@"flex|7 blade|seven blade|electric|high-performance|edelbrock|march|performance", Words_);
    private static readonly Regex RaceSumpWords = new(@"deep|milodon|moroso|canton|race|drag|hamburger|stef", Words_);

    private readonly PartsCatalog _catalog;
    private readonly Dictionary<string, double> _blockFactoryHp = new(StringComparer.OrdinalIgnoreCase);

    private EngineCooling(PartsCatalog catalog) => _catalog = catalog;

    /// <summary>Every block's factory power, from the builds; made once, as the builds are</summary>
    public static EngineCooling Create(PartsCatalog catalog, EngineBuildIndex builds)
    {
        var cooling = new EngineCooling(catalog);
        foreach (var block in builds.Runnable.Where(b => b.PowerHp > 0 && double.IsFinite(b.PowerHp)).GroupBy(b => b.BlockId, StringComparer.OrdinalIgnoreCase))
        {
            // The factory's builds, if any, else the kits and the notes: the least powerful is what the block was cooled for
            var factory = block.Where(b => b.Build.Origin == EngineBuild.OriginCar).ToList();
            cooling._blockFactoryHp[block.Key] = (factory.Count > 0 ? factory : block.ToList()).Min(b => b.PowerHp);
        }

        return cooling;
    }

    /// <summary>What a part does for the cooling, by its name (the source game gave these parts no figures)</summary>
    public static Kind KindOf(PartDefinition part)
    {
        var text = Words(part);
        if (PartKinds.Is(part, "Radiator") || part.SourceScript?.EndsWith(".Radiator", StringComparison.Ordinal) == true
            || RadiatorWord.IsMatch(text)) return Kind.Radiator;
        if (WaterPumpWords.IsMatch(text)) return Kind.WaterPump;
        if (FanWord.IsMatch(text)) return Kind.Fan;
        return Kind.Other;
    }

    /// <summary>A radiator that carries off more than a factory one</summary>
    public static bool IsPerformanceRadiator(PartDefinition radiator) => PerformanceRadiatorWords.IsMatch(Words(radiator));

    /// <summary>The least a block made as it left the factory, horsepower; null for a block no build rates</summary>
    public double? BlockFactoryHp(string blockId) => _blockFactoryHp.TryGetValue(blockId, out var hp) ? hp : null;

    /// <summary>
    /// The car's rating for the race mode.
    /// </summary>
    /// <param name="mounted">Every part on the car, mounted on it or on each other</param>
    /// <param name="enginePowerHp">What the engine makes now, on the dyno</param>
    /// <param name="factoryPowerHp">What the car's factory engine made; null when not known</param>
    /// <param name="factoryHadRadiator">The car's factory build came with a radiator: without one now, it has none</param>
    public EngineCoolingRating Rate(IEnumerable<PartInstance> mounted, double enginePowerHp, double? factoryPowerHp, bool factoryHadRadiator)
    {
        var parts = mounted.Select(p => _catalog.Get(p.DefinitionId)).OfType<PartDefinition>().ToList();
        var radiators = parts.Where(p => KindOf(p) == Kind.Radiator).ToList();
        var pumps = parts.Where(p => KindOf(p) == Kind.WaterPump).ToList();
        var fans = parts.Where(p => KindOf(p) == Kind.Fan).ToList();

        // What the cooling was sized for: the car's factory engine, or the block's own factory power after a swap
        var factory = factoryPowerHp is > 0 && double.IsFinite(factoryPowerHp.Value) ? factoryPowerHp.Value : 0;
        var block = parts.FirstOrDefault(PartKinds.IsBlock) is { } b ? BlockFactoryHp(b.Id) ?? 0 : 0;
        var sizedFor = Math.Max(factory, block);
        var power = double.IsFinite(enginePowerHp) && enginePowerHp > 0 ? enginePowerHp : sizedFor;

        double cooling;
        if (power <= 0 || sizedFor <= 0)
        {
            cooling = 1;
        }
        else if (radiators.Count > 0)
        {
            var carried = sizedFor * (radiators.Any(IsPerformanceRadiator) ? PerformanceRadiator : 1);
            if (pumps.Any(p => PerformancePumpWords.IsMatch(Words(p)))) carried *= PerformancePump;
            cooling = carried / power;
        }
        else
        {
            cooling = factoryHadRadiator ? NoRadiator : sizedFor / power;
        }

        double fan;
        if (radiators.Any(r => ElectricFanWords.IsMatch(Words(r)))) fan = ElectricFan;
        else if (fans.Count == 0) fan = radiators.Count > 0 || factoryHadRadiator ? NoFan : StockFan;
        else fan = fans.Any(f => PerformanceFanWords.IsMatch(Words(f))) ? PerformanceFan : StockFan;

        var pan = parts.FirstOrDefault(p => PartKinds.GroupOf(p) == OilPansGroup);
        return new EngineCoolingRating(Math.Round(Math.Clamp(cooling, 0.05, 3), 3), fan, SumpG(pan));
    }

    /// <summary>The g an oil pan holds its oil to</summary>
    public static double SumpG(PartDefinition? pan)
    {
        if (pan == null) return NoSumpG;
        if (!RaceSumpWords.IsMatch(Words(pan))) return StockSumpG;
        return pan.Number("capacity") >= BigSumpLitres ? BigSumpG : RaceSumpG;
    }

    /// <summary>The words a part goes by: its display name, its name and its id</summary>
    private static string Words(PartDefinition part) => $"{part.DisplayName} {part.Name} {part.Id}";
}
