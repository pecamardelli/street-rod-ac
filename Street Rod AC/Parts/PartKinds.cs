using System.Runtime.CompilerServices;

namespace Street_Rod_AC.Parts;

/// <summary>
/// What kind of thing a part is, read off its script classes: the class of the source game's own framework
/// nearest to the part ("OHV_Camshaft", "Transmission") is the reliable taxonomy, the catalog categories of
/// the mods are not.
/// </summary>
public static class PartKinds
{
    public const string Accessories = "Accessories";

    private const string FrameworkPackage = "java.game.parts.";

    // Shelf labels by framework class; matched by "ends with", first hit wins
    private static readonly (string ClassSuffix, string Group)[] Groups =
    {
        ("Block_Vee_OHV", "Engine blocks"), ("Block_Vee_OHC", "Engine blocks"), ("Block_Inline_OHV", "Engine blocks"),
        ("Block_Inline_OHC", "Engine blocks"), ("Block_Vee", "Engine blocks"), ("Block_Inline", "Engine blocks"), ("Block", "Engine blocks"),
        ("CylinderHead", "Cylinder heads"),
        ("Camshaft", "Camshafts"),
        ("Crankshaft", "Crankshafts"),
        ("ConnectingRod", "Connecting rods"),
        ("Piston", "Pistons"),
        ("Flywheel", "Flywheels and clutches"), ("Clutch", "Flywheels and clutches"),
        ("Transmission", "Transmissions"),
        ("IntakeManifold", "Intake manifolds"),
        ("FuelInjectorSystem", "Carburettors and injection"), ("AirFuelDeliverySystem", "Carburettors and injection"),
        ("AirFilter", "Air filters"),
        ("SuperCharger", "Superchargers and turbos"), ("TurboCharger", "Superchargers and turbos"),
        ("NOSInjectorSystem", "Nitrous"), ("Canister", "Nitrous"),
        ("ExhaustHeader", "Exhaust"), ("ExhaustPipe", "Exhaust"), ("ExhaustTip", "Exhaust"),
        ("OilPan", "Oil pans"),
        ("Tyre", Tyres), ("Wheel", Rims), ("Brake", Brakes), ("Spring", Springs), ("ShockAbsorber", Shocks),
        ("Swaybar", "Sway bars"), ("Suspension", "Suspension arms")
    };

    // The running gear: what goes on the car's own wheel slots, and what goes on it
    public const string Tyres = "Tyres";
    public const string Rims = "Rims";
    public const string Brakes = "Brakes";
    public const string Springs = "Springs";
    public const string Shocks = "Shock absorbers";

    /// <summary>
    /// Simple name of the framework class nearest to the part, "" for parts without a script class.
    /// Classes of the engine packs and cars sit under java.game.parts.engines / java.game.cars.
    /// </summary>
    public static string FrameworkClass(PartDefinition part)
    {
        foreach (var name in part.ClassChain)
        {
            if (!name.StartsWith(FrameworkPackage, StringComparison.Ordinal)) continue;
            if (name.StartsWith(FrameworkPackage + "engines.", StringComparison.Ordinal)) continue;

            return name[(name.LastIndexOf('.') + 1)..];
        }

        return string.Empty;
    }

    // A part's class chain does not change once it is loaded, and shops, the workbench and the engine factory ask
    // for its group per candidate in their loops: worked out once per part
    private static readonly ConditionalWeakTable<PartDefinition, string> GroupCache = new();

    /// <summary>Label of the shelf the part is found on in a shop</summary>
    public static string GroupOf(PartDefinition part) => GroupCache.GetValue(part, FindGroup);

    private static string FindGroup(PartDefinition part)
    {
        var kind = FrameworkClass(part);
        foreach (var (suffix, group) in Groups)
        {
            if (kind.EndsWith(suffix, StringComparison.Ordinal)) return group;
        }

        return Accessories;
    }

    /// <summary>
    /// Kinds that never mount on their own kind: a head does not go on a head, whatever a config says.
    /// Checked against every engine build. Intake manifolds are not among them: some go on a converter plate
    /// that is a manifold itself; clutches go on flywheels, accessories on accessories.
    /// </summary>
    public static bool NeverStacks(string group) => group is "Engine blocks" or "Cylinder heads" or "Camshafts" or "Crankshafts"
        or "Connecting rods" or "Pistons" or "Transmissions";

    public static bool IsBlock(PartDefinition part) => Is(part, "Block");

    /// <summary>True when the part's script descends from the class, given by simple name</summary>
    public static bool Is(PartDefinition part, string className) =>
        part.ClassChain.Any(c => c.EndsWith("." + className, StringComparison.Ordinal));
}
