using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;

namespace Street_Rod_AC.Parts.Cars;

/// <summary>
/// A car's damage as Assetto Corsa sees it, kept on the car's parts, and back.
///
/// After a race, what AC reports lands on the parts: an over-revved or crashed engine on its rotating parts
/// (crankshaft, rods, pistons, camshafts), the gearbox's damage on the transmission, a bent corner on its spring
/// and shock, the tread on the tyres. Body damage stays on the car (<see cref="Car.BodyDamageKmh"/>): a car's parts
/// have no panels. <c>Tear</c> is damage and only a repair takes it off; <c>Wear</c> is mileage.
///
/// Before a race the same parts give AC its start (<see cref="StartState"/>): the engine's life is its weakest
/// rotating part's, the gearbox's wear is the transmission's, each corner is as bent as its most damaged spring or
/// shock. The tyres go in as the data makes them (their wear lowers the grip in tyres.ini), and AC wears them from
/// new: carrying AC's tyre kilometres too would count the old wear twice.
///
/// Group names come from <see cref="PartKinds.GroupOf"/>, handed in as a lookup so the rules can be tested
/// without a catalog.
/// </summary>
public static class CarCondition
{
    public const int Zones = 4;

    public static readonly string[] ZoneNames = { "front", "rear", "left side", "right side" };

    /// <summary>A body that has taken this much, all zones together, is a write-off (a 200 km/h wreck reports 205 on two zones)</summary>
    public const double TotaledKmh = 200;

    /// <summary>AC's life of a new engine</summary>
    public const double NewEngineLife = 1000;

    /// <summary>A gearbox or a corner in worse shape than this does not go racing</summary>
    public const double BrokenBelow = 0.1;

    /// <summary>
    /// Wear from plain driving, per km, on the engine and the gearbox. Small: what a race does to a car is AC's damage;
    /// a drag race costs a few thousandths of a percent.
    /// </summary>
    public const double MileageWearPerKm = 1.0 / 40000;

    public const string TransmissionGroup = "Transmissions";

    private static readonly HashSet<string> RotatingGroups = new(StringComparer.Ordinal)
    {
        "Crankshafts", "Connecting rods", "Pistons", "Camshafts"
    };

    /// <summary>A lookup from part id to its shelf group, off the catalog</summary>
    public static Func<string, string?> Groups(PartsCatalog catalog) =>
        id => catalog.Get(id) is { } definition ? PartKinds.GroupOf(definition) : null;

    // ----- what the car is in -----

    /// <summary>The four zones, whatever an older save or a bad file left there</summary>
    public static double[] Body(Car car)
    {
        var zones = new double[Zones];
        var saved = car.BodyDamageKmh ?? Array.Empty<double>();
        for (var i = 0; i < Zones && i < saved.Length; i++) zones[i] = Clean(saved[i]);
        return zones;
    }

    public static double BodyTotal(Car car) => Body(car).Sum();

    public static bool IsTotaled(Car car) => BodyTotal(car) >= TotaledKmh;

    /// <summary>1 for a straight body, 0 for a write-off</summary>
    public static double BodyCondition(Car car) => 1 - Math.Min(1, BodyTotal(car) / TotaledKmh);

    public static IEnumerable<PartInstance> RotatingParts(Car car, Func<string, string?> groupOf) =>
        car.Engine?.SelfAndDescendants().Where(p => groupOf(p.DefinitionId) is { } g && RotatingGroups.Contains(g)) ?? Enumerable.Empty<PartInstance>();

    public static PartInstance? Transmission(Car car, Func<string, string?> groupOf) =>
        car.Engine?.SelfAndDescendants().FirstOrDefault(p => groupOf(p.DefinitionId) == TransmissionGroup);

    /// <summary>AC's engine life: its weakest rotating part's; an engine without any is taken as it is</summary>
    public static double EngineLife(Car car, Func<string, string?> groupOf)
    {
        var tears = RotatingParts(car, groupOf).Select(p => Clamp01(p.Tear)).ToList();
        return NewEngineLife * (tears.Count == 0 ? 1 : tears.Min());
    }

    public static double GearboxWear(Car car, Func<string, string?> groupOf) =>
        1 - Clamp01(Transmission(car, groupOf)?.Tear ?? 1);

    /// <summary>How bent each corner is, 0 to 1: its most damaged spring or shock</summary>
    public static double[] SuspensionBend(Car car)
    {
        var corners = RunningGear.Mounted(car.Parts);
        return corners.Select(c => 1 - Math.Min(Clamp01(c.Spring?.Tear ?? 1), Clamp01(c.Shock?.Tear ?? 1))).ToArray();
    }

    /// <summary>What the race mode puts into AC for this car at the start</summary>
    public static RaceStartState StartState(Car car, Func<string, string?>? groupOf)
    {
        var withParts = groupOf != null && car.HasPartsAssigned;
        return new RaceStartState
        {
            BodyKmh = Body(car),
            EngineLife = withParts ? EngineLife(car, groupOf!) : NewEngineLife * Clamp01(car.EngineHealth),
            GearboxWear = withParts ? GearboxWear(car, groupOf!) : 1 - Clamp01(car.TransmissionHealth),
            SuspensionBend = groupOf != null && car.HasRunningGearAssigned ? SuspensionBend(car) : new double[4]
        };
    }

    /// <summary>
    /// A start the car can at least leave the line with. For an opponent's car, which races whatever shape it is in
    /// until opponents look after their cars (roadmap step 5): its save keeps the damage, the race gets it running.
    /// </summary>
    public static RaceStartState Runnable(RaceStartState state)
    {
        var body = state.BodyKmh.ToArray();
        var total = body.Sum();
        var most = TotaledKmh * 0.9;
        if (total > most) body = body.Select(z => z * most / total).ToArray();

        return new RaceStartState
        {
            BodyKmh = body,
            EngineLife = Math.Max(state.EngineLife, NewEngineLife * 0.3),
            GearboxWear = Math.Min(state.GearboxWear, 0.7),
            SuspensionBend = state.SuspensionBend.Select(b => Math.Min(b, 0.7)).ToArray()
        };
    }

    /// <summary>What keeps the car from racing, in words for the player; empty when it can go</summary>
    public static List<string> WhyCannotRace(Car car, Func<string, string?>? groupOf)
    {
        var problems = new List<string>();
        if (IsTotaled(car)) problems.Add("the body is wrecked: the car is totaled");

        if (groupOf != null && car.HasPartsAssigned)
        {
            if (car.Engine != null && EngineLife(car, groupOf) <= 0) problems.Add("the engine is blown");
            if (Transmission(car, groupOf) is { } gearbox && gearbox.Tear < BrokenBelow) problems.Add("the gearbox is wrecked");
        }
        else
        {
            if (car.EngineHealth <= 0) problems.Add("the engine is blown");
            if (car.TransmissionHealth < BrokenBelow) problems.Add("the gearbox is wrecked");
        }

        if (groupOf != null && car.HasRunningGearAssigned)
        {
            var corners = RunningGear.Mounted(car.Parts);
            var bend = SuspensionBend(car);
            for (var i = 0; i < corners.Length; i++)
            {
                if (1 - bend[i] < BrokenBelow) problems.Add($"the {RunningGear.CornerNames[i]} suspension is broken");
                if (corners[i].Tyre is { Tear: <= 0 }) problems.Add($"the {RunningGear.CornerNames[i]} tyre is blown");
            }
        }

        return problems;
    }

    // ----- after a race -----

    /// <summary>
    /// Puts what AC reported at the end of a race onto the car and its parts, and brings the car's own figures up to
    /// date. Returns what happened to the car, a line each, for the player.
    /// </summary>
    /// <param name="groupOf">Null when there is no parts catalog: the car's own figures take it all</param>
    public static List<string> ApplyRace(Car car, RaceCarCondition condition, double distanceKm, Func<string, string?>? groupOf)
    {
        var report = new List<string>();
        ApplyBody(car, condition, report);

        if (groupOf != null && car.HasPartsAssigned)
        {
            ApplyEngine(car, condition, groupOf, report);
            ApplyGearbox(car, condition, groupOf, report);
            ApplyMileage(car, distanceKm);
        }
        else
        {
            ApplyToFigures(car, condition, report);
        }

        if (groupOf != null && car.HasRunningGearAssigned) ApplyRunningGear(car, condition, report);

        if (groupOf != null) RefreshFigures(car, groupOf);
        else car.BodyCondition = BodyCondition(car);

        return report;
    }

    private static void ApplyBody(Car car, RaceCarCondition condition, List<string> report)
    {
        if (condition.BodyDamageKmh is not { } reported) return;

        // AC starts the race with the body as the car had it: what it reports is the whole of it. A start that did not
        // take (no physics API) reports less than the car had, so the worse of the two stands.
        var zones = Body(car);
        var wasTotaled = zones.Sum() >= TotaledKmh;
        for (var i = 0; i < Zones && i < reported.Count; i++)
        {
            var now = Math.Min(Clean(reported[i]), 1000);
            if (now > zones[i] + 0.5) report.Add($"Body: the {ZoneNames[i]} took a {now - zones[i]:0} km/h hit.");
            zones[i] = Math.Max(zones[i], now);
        }

        car.BodyDamageKmh = zones;
        if (!wasTotaled && zones.Sum() >= TotaledKmh) report.Add("The body is wrecked: the car is totaled.");
    }

    private static void ApplyEngine(Car car, RaceCarCondition condition, Func<string, string?> groupOf, List<string> report)
    {
        if (condition.EngineLife is not { } life || !double.IsFinite(life)) return;

        // AC reports the life left, having started from the car's: the rotating parts are no better than that
        var left = Clamp01(life / NewEngineLife);
        var before = EngineLife(car, groupOf) / NewEngineLife;
        foreach (var part in RotatingParts(car, groupOf)) part.Tear = Math.Min(Clamp01(part.Tear), left);

        if (left <= 0 && before > 0) report.Add("Engine: it blew. It needs a rebuild before it runs again.");
        else if (left < before - 0.005) report.Add($"Engine: the bottom end took a beating ({before * 100:0}% → {left * 100:0}%).");
    }

    private static void ApplyGearbox(Car car, RaceCarCondition condition, Func<string, string?> groupOf, List<string> report)
    {
        if (condition.GearboxDamage is not { } damage || Transmission(car, groupOf) is not { } gearbox) return;

        // AC starts every race with a new gearbox: what it reports is this race's
        damage = Clamp01(Clean(damage));
        if (damage <= 0) return;
        var before = Clamp01(gearbox.Tear);
        gearbox.Tear = Math.Max(0, before - damage);
        report.Add(gearbox.Tear < BrokenBelow
            ? "Gearbox: it is wrecked."
            : $"Gearbox: missed shifts hurt it ({before * 100:0}% → {gearbox.Tear * 100:0}%).");
    }

    private static void ApplyMileage(Car car, double distanceKm)
    {
        var wear = Clean(distanceKm) * MileageWearPerKm;
        if (wear <= 0 || car.Engine == null) return;
        foreach (var part in car.Engine.SelfAndDescendants()) part.Wear = Math.Max(0, Clamp01(part.Wear) - wear);
    }

    private static void ApplyRunningGear(Car car, RaceCarCondition condition, List<string> report)
    {
        if (condition.Wheels is not { } wheels) return;

        var corners = RunningGear.Mounted(car.Parts);
        for (var i = 0; i < corners.Length && i < wheels.Count; i++)
        {
            var wheel = wheels[i];
            var corner = corners[i];
            var name = RunningGear.CornerNames[i];

            if (corner.Tyre is { } tyre)
            {
                tyre.Wear = Math.Max(0, Clamp01(tyre.Wear) - Clamp01(Clean(wheel.TyreWear ?? 0)));
                if (wheel.TyreBlown == true && tyre.Tear > 0)
                {
                    tyre.Tear = 0;
                    report.Add($"Tyres: the {name} tyre blew.");
                }
            }

            // AC starts every race with straight corners: the bend it reports is this race's
            var bend = Clamp01(Clean(wheel.SuspensionDamage ?? 0) / RaceStartState.MaxSuspensionBendMetres);
            if (bend <= 0.005) continue;
            foreach (var part in new[] { corner.Spring, corner.Shock })
            {
                if (part != null) part.Tear = Math.Max(0, Clamp01(part.Tear) - bend);
            }

            report.Add($"Suspension: the {name} corner is bent.");
        }
    }

    /// <summary>A car without parts: AC's report goes on its own figures</summary>
    private static void ApplyToFigures(Car car, RaceCarCondition condition, List<string> report)
    {
        if (condition.EngineLife is { } life && double.IsFinite(life))
        {
            var left = Clamp01(life / NewEngineLife);
            if (left <= 0 && car.EngineHealth > 0) report.Add("Engine: it blew. It needs a rebuild before it runs again.");
            car.EngineHealth = Math.Min(Clamp01(car.EngineHealth), left);
        }

        if (condition.GearboxDamage is { } damage)
            car.TransmissionHealth = Math.Max(0, Clamp01(car.TransmissionHealth) - Clamp01(Clean(damage)));

        if (condition.Wheels is { Count: > 0 } wheels)
            car.TireCondition = Math.Max(0, Clamp01(car.TireCondition) - wheels.Average(w => Clamp01(Clean(w.TyreWear ?? 0))));
    }

    /// <summary>
    /// The car's own figures (what prices it and what the garage shows) worked out from its parts: the engine is
    /// its parts' mileage times the life of its weakest rotating part, the gearbox and tyres their parts' shape, the
    /// body from its damage.
    /// </summary>
    public static void RefreshFigures(Car car, Func<string, string?> groupOf)
    {
        if (car.HasPartsAssigned && car.Engine is { } engine)
        {
            var gearbox = Transmission(car, groupOf);
            var engineParts = engine.SelfAndDescendants().Where(p => !ReferenceEquals(p, gearbox)).ToList();
            var mileage = engineParts.Count == 0 ? 1 : engineParts.Average(p => Clamp01(p.Wear));
            car.EngineHealth = mileage * EngineLife(car, groupOf) / NewEngineLife;
            if (gearbox != null) car.TransmissionHealth = Clamp01(gearbox.Wear) * Clamp01(gearbox.Tear);
        }

        if (car.HasRunningGearAssigned)
        {
            var tyres = RunningGear.Mounted(car.Parts).Select(c => c.Tyre).Where(t => t != null).ToList();
            if (tyres.Count > 0) car.TireCondition = tyres.Average(t => Clamp01(t!.Wear) * Clamp01(t.Tear));
        }

        car.BodyCondition = BodyCondition(car);
    }

    private static double Clamp01(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1;

    /// <summary>A reported figure, not negative and a number</summary>
    private static double Clean(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;
}
