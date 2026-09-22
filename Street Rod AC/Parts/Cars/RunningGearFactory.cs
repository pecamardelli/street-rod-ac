using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts.Export;

namespace Street_Rod_AC.Parts.Cars;

/// <summary>
/// The running gear a car leaves the factory with: for each axle the catalog's tyre, rim, brake, spring and
/// shock nearest to what the car's own Assetto Corsa data describes. The choice is deterministic, so the same
/// car always gets the same factory parts and what the player mounts can be measured against them.
/// </summary>
public static class RunningGearFactory
{
    /// <summary>The factory parts of one axle</summary>
    public sealed record AxleParts(PartDefinition Tyre, PartDefinition Rim, PartDefinition Brake, PartDefinition Spring, PartDefinition Shock);

    /// <returns>The factory parts of both axles, null when the catalog lacks a kind</returns>
    public static (AxleParts Front, AxleParts Rear)? Choose(PartsCatalog catalog, AcCarSpecs specs)
    {
        var tyres = ByGroup(catalog, PartKinds.Tyres);
        var rims = ByGroup(catalog, PartKinds.Rims);
        var brakes = ByGroup(catalog, PartKinds.Brakes);
        var springs = ByGroup(catalog, PartKinds.Springs);
        var shocks = ByGroup(catalog, PartKinds.Shocks);
        if (tyres.Count == 0 || rims.Count == 0 || brakes.Count == 0 || springs.Count == 0 || shocks.Count == 0) return null;

        // A tyre is only as good as a rim it goes on
        var fitted = tyres.Where(t => rims.Any(r => RunningGear.TyreFitsRim(t, r))).ToList();
        if (fitted.Count == 0) return null;

        // The tyre nearest in width, the plainest compound of that width, then the nearest rolling radius.
        // One tyre for the whole car unless the car is clearly staggered: what a swap does should read the
        // same at both ends.
        PartDefinition Tyre(double width, double radius) => fitted
            .OrderBy(t => Math.Abs(RunningGear.TyreWidth(t) - width))
            .ThenBy(t => RunningGear.TyreGrip(t))
            .ThenBy(t => Math.Abs(RunningGear.TyreRadius(t) - radius))
            .ThenBy(t => t.Id, StringComparer.Ordinal).First();
        var frontTyre = Tyre(specs.Front.TyreWidth, specs.Front.TyreRadius);
        var rearTyre = Math.Abs(specs.Rear.TyreWidth - specs.Front.TyreWidth) < 0.03 ? frontTyre : Tyre(specs.Rear.TyreWidth, specs.Rear.TyreRadius);

        AxleParts Axle(AcAxleSpecs axle, PartDefinition tyre)
        {
            // The rim the plainest that takes the tyre; the rest by the figure nearest to the car's own, which
            // is the smallest the catalog has more often than not: what the shop sells is an upgrade
            var rim = rims.Where(r => RunningGear.TyreFitsRim(tyre, r))
                .OrderBy(r => r.Number("rim_type")).ThenBy(r => Math.Abs(RunningGear.RimOffset(r)))
                .ThenBy(r => r.Number("value")).ThenBy(r => r.Id, StringComparer.Ordinal).First();
            var brake = brakes.OrderBy(b => Math.Abs(RunningGear.BrakeTorque(b) - axle.BrakeTorque))
                .ThenBy(b => b.Number("value")).ThenBy(b => b.Id, StringComparer.Ordinal).First();
            var spring = springs.OrderBy(s => Math.Abs(RunningGear.SpringRate(s) - axle.SpringRate))
                .ThenBy(s => Math.Abs(RunningGear.SpringDesignLoad(s) - axle.CornerLoad)).ThenBy(s => s.Id, StringComparer.Ordinal).First();
            var shock = shocks.OrderBy(s => Math.Abs(RunningGear.Damping(s).Bump - axle.DampBump))
                .ThenBy(s => Math.Abs(RunningGear.Damping(s).Rebound - axle.DampRebound)).ThenBy(s => s.Id, StringComparer.Ordinal).First();
            return new AxleParts(tyre, rim, brake, spring, shock);
        }

        return (Axle(specs.Front, frontTyre), Axle(specs.Rear, rearTyre));
    }

    /// <summary>The factory running gear as parts on the car's wheel slots, worn like the car</summary>
    public static List<PartInstance> Create(PartsCatalog catalog, AcCarSpecs specs, double condition)
    {
        var parts = new List<PartInstance>();
        if (Choose(catalog, specs) is not var (front, rear)) return parts;

        var wear = Math.Clamp(condition, 0, 1);
        for (var corner = 0; corner < RunningGear.Corners; corner++)
        {
            var axle = RunningGear.IsFront(corner) ? front : rear;

            var rim = Instance(axle.Rim, RunningGear.WheelSlot(corner), 1, wear);
            rim.Children.Add(Instance(axle.Tyre, RunningGear.TyreSlotOnRim, 1, wear));
            parts.Add(rim);
            parts.Add(Instance(axle.Brake, RunningGear.BrakeSlot(corner), 1, wear));
            parts.Add(Instance(axle.Spring, RunningGear.SpringSlot(corner), 1, wear));
            parts.Add(Instance(axle.Shock, RunningGear.ShockSlot(corner), 1, wear));
        }

        return parts;
    }

    private static PartInstance Instance(PartDefinition definition, int parentSlot, int ownSlot, double wear) => new(definition.Id)
    {
        ParentSlot = parentSlot,
        OwnSlot = ownSlot,
        Wear = wear
    };

    private static List<PartDefinition> ByGroup(PartsCatalog catalog, string group) =>
        catalog.Parts.Values.Where(p => p.IsScripted && PartKinds.GroupOf(p) == group).OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
}
