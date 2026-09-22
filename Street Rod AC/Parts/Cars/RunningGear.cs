using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Parts.Cars;

/// <summary>What one corner of a car carries: the rim with its tyre, the brake, the spring and the shock</summary>
public sealed class CornerParts
{
    public PartInstance? Rim { get; set; }
    public PartInstance? Tyre { get; set; }
    public PartInstance? Brake { get; set; }
    public PartInstance? Spring { get; set; }
    public PartInstance? Shock { get; set; }

    public IEnumerable<PartInstance> All => new[] { Rim, Tyre, Brake, Spring, Shock }.Where(p => p != null)!;
}

/// <summary>
/// The running gear of a car: what sits on the car's own wheel slots, numbered as the part scripts number them
/// (the chassis script reads rim 101+i, brake 111+i, shock 301+i, spring 311+i for wheel i; 0-1 front, 2-3 rear,
/// even numbers left). The tyre goes on the rim. What each part does to the car is read off its script fields
/// with the framework's own formulas.
/// </summary>
public static class RunningGear
{
    public const int Corners = 4;
    public const int TyreSlotOnRim = 2;

    public static readonly string[] CornerNames = { "front left", "front right", "rear left", "rear right" };

    public static int WheelSlot(int corner) => 101 + corner;
    public static int BrakeSlot(int corner) => 111 + corner;
    public static int ShockSlot(int corner) => 301 + corner;
    public static int SpringSlot(int corner) => 311 + corner;

    public static bool IsFront(int corner) => corner < 2;

    /// <summary>The corner a car slot belongs to, -1 for slots that are not running gear</summary>
    public static int CornerOf(int carSlot) => carSlot switch
    {
        >= 101 and < 101 + Corners => carSlot - 101,
        >= 111 and < 111 + Corners => carSlot - 111,
        >= 301 and < 301 + Corners => carSlot - 301,
        >= 311 and < 311 + Corners => carSlot - 311,
        _ => -1
    };

    /// <summary>The car slots a part of a group goes on, none for parts that do not sit on the car itself</summary>
    public static IEnumerable<int> CarSlotsFor(string group)
    {
        Func<int, int>? slot = group switch
        {
            PartKinds.Rims => WheelSlot,
            PartKinds.Brakes => BrakeSlot,
            PartKinds.Springs => SpringSlot,
            PartKinds.Shocks => ShockSlot,
            _ => null
        };
        return slot == null ? Enumerable.Empty<int>() : Enumerable.Range(0, Corners).Select(slot);
    }

    public static bool IsRunningGear(string group) => group is PartKinds.Rims or PartKinds.Tyres or PartKinds.Brakes or PartKinds.Springs or PartKinds.Shocks;

    /// <summary>The tyre script's own fit check: same rim diameter, rim width within what the tyre takes</summary>
    public static bool TyreFitsRim(PartDefinition tyre, PartDefinition rim)
    {
        var tyreRim = tyre.Number("wheel_radius");             // mm
        var rimRadius = rim.Number("wheel_radius") * 1000;     // m
        if (Math.Abs(tyreRim - rimRadius) > 0.5) return false;

        var width = rim.Number("rim_width");
        var (min, max) = (tyre.Number("minRimWidth"), tyre.Number("maxRimWidth"));
        return (min <= 0 || width >= min - 0.01) && (max <= 0 || width <= max + 0.01);
    }

    /// <summary>The parts on the car's wheel slots, by corner</summary>
    public static CornerParts[] Mounted(List<PartInstance> carParts)
    {
        var corners = Enumerable.Range(0, Corners).Select(_ => new CornerParts()).ToArray();
        foreach (var part in carParts)
        {
            var corner = CornerOf(part.ParentSlot);
            if (corner < 0) continue;

            var slot = part.ParentSlot;
            if (slot == WheelSlot(corner))
            {
                corners[corner].Rim = part;
                corners[corner].Tyre = part.Children.FirstOrDefault(c => c.ParentSlot == TyreSlotOnRim);
            }
            else if (slot == BrakeSlot(corner)) corners[corner].Brake = part;
            else if (slot == SpringSlot(corner)) corners[corner].Spring = part;
            else if (slot == ShockSlot(corner)) corners[corner].Shock = part;
        }

        return corners;
    }

    public static bool HasAny(List<PartInstance> carParts) => carParts.Any(p => CornerOf(p.ParentSlot) >= 0);

    // ----- what the parts come to, in the scripts' formulas -----

    /// <summary>Brake torque at full pedal, Nm: clamp force over the calipers times pad on disc at the disc's radius, less with wear</summary>
    public static double BrakeTorque(PartDefinition brake, double wear = 1)
    {
        var calipers = Math.Max(1, brake.Number("number_of_calipers", 1));
        var torque = brake.Number("force") * (1 + (calipers - 1) * 0.333) * brake.Number("friction", 1) * brake.Number("radius");
        return (0.2 + Math.Sqrt(Math.Clamp(wear, 0, 1)) * 0.8) * torque;
    }

    /// <summary>Wheel rate, N/m</summary>
    public static double SpringRate(PartDefinition spring) => spring.Number("force");

    /// <summary>The load the spring was made for, kg on the wheel</summary>
    public static double SpringDesignLoad(PartDefinition spring) => spring.Number("designedMassOnWheel");

    /// <summary>Bump and rebound damping, N per m/s; the shock's rebound follows what is inside it</summary>
    public static (double Bump, double Rebound) Damping(PartDefinition shock)
    {
        var bump = shock.Number("damping");
        return (bump, bump * shock.Number("rebound_factor", 1));
    }

    /// <summary>Tread width in metres</summary>
    public static double TyreWidth(PartDefinition tyre) => tyre.Number("tyre_width") / 1000;

    /// <summary>Rolling radius in metres</summary>
    public static double TyreRadius(PartDefinition tyre) => tyre.Number("radius");

    /// <summary>Rim seat radius in metres</summary>
    public static double TyreRimRadius(PartDefinition tyre) => tyre.Number("wheel_radius") / 1000;

    /// <summary>Peak friction the tyre offers, a little less as it wears down to the cords</summary>
    public static double TyreGrip(PartDefinition tyre, double wear = 1) => tyre.Number("friction") * (0.85 + 0.15 * Math.Clamp(wear, 0, 1));

    public static double TyreLoadCapacity(PartDefinition tyre) => tyre.Number("loadcap");

    public static double TyreRollingResistance(PartDefinition tyre) => tyre.Number("rollres");

    /// <summary>Recommended inflation, bar</summary>
    public static double TyrePressure(PartDefinition tyre) => tyre.Number("optimal_inflation", 2);

    /// <summary>Wheel offset in metres; negative moves the wheel outwards</summary>
    public static double RimOffset(PartDefinition rim) => rim.Number("offset");

    public static double RimWidth(PartDefinition rim) => rim.Number("rim_width");
}
