using System.Numerics;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Parts.Logic;

namespace Street_Rod_AC.Parts;

/// <summary>Wheel hub centres of a car, in the space the car is drawn in</summary>
public sealed record CarAnchors(Vector3 WheelLF, Vector3 WheelRF, Vector3 WheelLR, Vector3 WheelRR)
{
    public Vector3 FrontAxle => (WheelLF + WheelRF) / 2f;
    public Vector3 RearAxle => (WheelLR + WheelRR) / 2f;
    public float Wheelbase => Vector3.Distance(FrontAxle, RearAxle);

    /// <summary>Hub of a corner as the running gear numbers them: 0 front left, 1 front right, 2 rear left, 3 rear right</summary>
    public Vector3 Hub(int corner) => corner switch { 0 => WheelLF, 1 => WheelRF, 2 => WheelLR, _ => WheelRR };

    public Vector3 Axle(int corner) => RunningGear.IsFront(corner) ? FrontAxle : RearAxle;

    /// <summary>Unit vector from the rear axle to the front axle, level with the ground</summary>
    public Vector3 Forward
    {
        get
        {
            var direction = FrontAxle - RearAxle;
            direction.Y = 0f;
            return direction.LengthSquared() < 1e-6f ? Vector3.UnitZ : Vector3.Normalize(direction);
        }
    }
}

/// <summary>
/// Estimates where a car's parts sit from its wheel hubs alone, since AC cars know nothing about
/// engine bays or suspension mounts: a front, longitudinal engine with the gearbox behind it, the wheel
/// and the brake on the hub, a coil spring and a shock next to every wheel.
/// Part models have their front towards -Z while AC cars face +Z, hence the frames below.
/// </summary>
public static class CarPartsLayout
{
    // Engine: crankshaft a little above the hubs, block centre behind the front axle line
    private const float CrankAboveHubs = 0.06f;
    private const float EngineBehindFrontAxle = 0.18f;
    private static readonly Vector3 DefaultCrankPosition = new(0f, -0.18f, 0f);

    // Running gear, relative to a hub: towards the car's centre line and up
    private const float BrakeInboard = 0.06f;
    private const float SpringInboard = 0.24f;
    private const float SpringSeatBelowHub = 0.06f;

    /// <param name="engine">The car's engine block with everything on it; null for an empty engine bay</param>
    /// <param name="gear">What sits on the car's wheel slots, each with what is on it (the tyre on the rim)</param>
    public static List<PlacedPart> Build(PartsCatalog catalog, CarAnchors anchors, InstalledPart? engine, IReadOnlyList<CarPart>? gear = null)
    {
        var result = new List<PlacedPart>();
        var forward = anchors.Forward;

        if (engine != null) result.AddRange(PartAssembler.Assemble(engine, EnginePlacement(anchors, engine.Definition)));

        foreach (var part in gear ?? Array.Empty<CarPart>())
        {
            var corner = RunningGear.CornerOf(part.CarSlot);
            if (corner < 0) continue;

            var hub = anchors.Hub(corner);
            var inboard = anchors.Axle(corner) - hub;
            inboard.Y = 0f;
            if (inboard.LengthSquared() < 1e-6f) continue;
            inboard = Vector3.Normalize(inboard);

            // Every wheel-side part is modelled for one side; turning it around the vertical axis gives the other
            var side = SideFrame(forward, inboard);
            var slot = part.CarSlot;

            if (slot == RunningGear.WheelSlot(corner)) result.AddRange(PartAssembler.Assemble(part.Root, BySlot(part.Root.Definition, side, hub)));
            else if (slot == RunningGear.BrakeSlot(corner)) result.AddRange(PartAssembler.Assemble(part.Root, BySlot(part.Root.Definition, side, hub + inboard * BrakeInboard)));
            else
            {
                var springSeat = hub + inboard * SpringInboard - Vector3.UnitY * SpringSeatBelowHub;
                result.AddRange(PartAssembler.Assemble(part.Root, BySlot(part.Root.Definition, side, springSeat)));
            }
        }

        return result;
    }

    /// <summary>Where an engine block sits in the car: crankshaft at the hubs' height, block behind the front axle line</summary>
    public static Matrix4x4 EnginePlacement(CarAnchors anchors, PartDefinition block)
    {
        var frame = CarFrame(anchors.Forward);

        var crank = block.Slots.FirstOrDefault(s => s.Name.Contains("crank", StringComparison.OrdinalIgnoreCase));
        var crankLocal = crank == null ? DefaultCrankPosition : new Vector3(crank.Position[0], crank.Position[1], 0f);
        var crankTarget = anchors.FrontAxle - anchors.Forward * EngineBehindFrontAxle + Vector3.UnitY * CrankAboveHubs;
        var origin = crankTarget - Vector3.TransformNormal(crankLocal, frame);

        return frame * Matrix4x4.CreateTranslation(origin);
    }

    /// <summary>Places a part so that its first slot (where it meets the car) lands on a point</summary>
    private static Matrix4x4 BySlot(PartDefinition part, Matrix4x4 frame, Vector3 target)
    {
        var slot = part.Slots.FirstOrDefault(s => s.Id == 1) ?? part.Slots.FirstOrDefault();
        var slotLocal = slot == null ? Vector3.Zero : new Vector3(slot.Position[0], slot.Position[1], slot.Position[2]);
        var origin = target - Vector3.TransformNormal(slotLocal, frame);
        return frame * Matrix4x4.CreateTranslation(origin);
    }

    /// <summary>Rotation that turns a part model (front towards -Z) to face the way the car does</summary>
    private static Matrix4x4 CarFrame(Vector3 forward)
    {
        var right = Vector3.Cross(Vector3.UnitY, forward);
        return Basis(-right, Vector3.UnitY, -forward);
    }

    /// <summary>Rotation for a wheel-side part: its +X ends up pointing away from the car</summary>
    private static Matrix4x4 SideFrame(Vector3 forward, Vector3 inboard)
    {
        var outboard = -inboard;
        return Basis(outboard, Vector3.UnitY, Vector3.Cross(outboard, Vector3.UnitY));
    }

    private static Matrix4x4 Basis(Vector3 x, Vector3 y, Vector3 z) => new(
        x.X, x.Y, x.Z, 0f,
        y.X, y.Y, y.Z, 0f,
        z.X, z.Y, z.Z, 0f,
        0f, 0f, 0f, 1f);
}
