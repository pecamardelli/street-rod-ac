using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Services.Time;

namespace Street_Rod_AC.Parts.Cars;

/// <summary>One piece of work the garage can do on a car: what it costs, how long it takes, and doing it</summary>
public sealed class RepairJob
{
    public RepairJob(string name, string detail, decimal cost, GameAction time, Action apply)
    {
        Name = name;
        Detail = detail;
        Cost = cost;
        Time = time;
        _apply = apply;
    }

    private readonly Action _apply;

    public string Name { get; }

    /// <summary>What is wrong, in a few words</summary>
    public string Detail { get; }

    public decimal Cost { get; }

    /// <summary><see cref="GameAction.GarageWorkMinor"/> or <see cref="GameAction.GarageWorkMajor"/></summary>
    public GameAction Time { get; }

    /// <summary>Does the work on the car; money and time are the caller's</summary>
    public void Apply() => _apply();
}

/// <summary>
/// Repairs: what takes damage (<c>Tear</c>, and the body) off a car. Mileage (<c>Wear</c>) is not repaired: a worn
/// part is replaced, in the parts view. A job costs a share of the damaged parts' new price, as much of it as the
/// damage goes, plus the labour; the body shop charges by how hard the body was hit. Small jobs take half an hour,
/// big ones two hours.
/// </summary>
public static class RepairShop
{
    /// <summary>Share of a part's new price a repair of a fully wrecked part costs</summary>
    public const double PartShare = 0.6;

    public const decimal Labour = 15m;

    /// <summary>Body work, per km/h of collision the body took</summary>
    public const decimal BodyCostPerKmh = 8m;

    /// <summary>Less damage than this on a body is a small job</summary>
    public const double SmallBodyJobKmh = 40;

    /// <summary>A part at this or better needs nothing done</summary>
    private const double Straight = 0.995;

    /// <param name="catalog">Null when there are no parts: only the body can be done</param>
    public static List<RepairJob> Jobs(Car car, PartsCatalog? catalog)
    {
        var jobs = new List<RepairJob>();
        Func<string, string?>? groupOf = catalog == null ? null : CarCondition.Groups(catalog);
        void Refresh()
        {
            if (groupOf != null) CarCondition.RefreshFigures(car, groupOf);
            else car.BodyCondition = CarCondition.BodyCondition(car);
        }

        var body = CarCondition.BodyTotal(car);
        if (body > 0.5)
        {
            var detail = CarCondition.IsTotaled(car) ? "Totaled: the whole body needs redoing" : $"Dents and scratches ({body:0} km/h of hits)";
            jobs.Add(new RepairJob("Body work", detail, Labour + Math.Round(BodyCostPerKmh * (decimal)body),
                body < SmallBodyJobKmh ? GameAction.GarageWorkMinor : GameAction.GarageWorkMajor,
                () => { car.BodyDamageKmh = new double[CarCondition.Zones]; Refresh(); }));
        }

        if (catalog == null || groupOf == null) return jobs;

        var gearbox = CarCondition.Transmission(car, groupOf);
        if (car.Engine is { } engine)
        {
            var torn = engine.SelfAndDescendants().Where(p => !ReferenceEquals(p, gearbox) && p.Tear < Straight).ToList();
            if (torn.Count > 0)
            {
                var life = CarCondition.EngineLife(car, groupOf) / CarCondition.NewEngineLife;
                var detail = life <= 0 ? "Blown" : $"Damaged internals ({life * 100:0}% of its life left)";
                jobs.Add(PartsJob("Engine rebuild", detail, torn, catalog, GameAction.GarageWorkMajor, Refresh));
            }
        }

        if (gearbox is { Tear: < Straight })
        {
            var detail = gearbox.Tear < CarCondition.BrokenBelow ? "Wrecked" : $"Worn by missed shifts ({gearbox.Tear * 100:0}%)";
            jobs.Add(PartsJob("Gearbox rebuild", detail, new[] { gearbox }, catalog,
                gearbox.Tear < 0.5 ? GameAction.GarageWorkMajor : GameAction.GarageWorkMinor, Refresh));
        }

        var corners = RunningGear.Mounted(car.Parts);
        for (var i = 0; i < corners.Length; i++)
        {
            var name = RunningGear.CornerNames[i];
            var bent = new[] { corners[i].Spring, corners[i].Shock }.Where(p => p is { Tear: < Straight }).Select(p => p!).ToList();
            if (bent.Count > 0)
                jobs.Add(PartsJob($"Straighten the {name} corner", "Bent in a hit", bent, catalog, GameAction.GarageWorkMinor, Refresh));

            if (corners[i].Tyre is { Tear: < Straight } tyre && catalog.Get(tyre.DefinitionId) is { } tyreDefinition)
            {
                // A blown tyre is not patched: the same tyre, new, goes on
                jobs.Add(new RepairJob($"New {name} tyre", "Blown", Labour + PartPricing.Round(PartPricing.NewPrice(tyreDefinition)),
                    GameAction.GarageWorkMinor, () => { tyre.Tear = 1; tyre.Wear = 1; Refresh(); }));
            }
        }

        return jobs;
    }

    private static RepairJob PartsJob(string name, string detail, IReadOnlyList<PartInstance> parts, PartsCatalog catalog, GameAction time, Action refresh)
    {
        var cost = parts.Sum(p => catalog.Get(p.DefinitionId) is { } definition
            ? PartPricing.NewPrice(definition) * PartShare * (1 - Math.Clamp(p.Tear, 0, 1))
            : 0);
        return new RepairJob(name, detail, Labour + PartPricing.Round(cost), time, () =>
        {
            foreach (var part in parts) part.Tear = 1;
            refresh();
        });
    }
}
