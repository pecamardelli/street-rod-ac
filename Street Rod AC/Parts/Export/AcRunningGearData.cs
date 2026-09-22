using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts.Cars;

namespace Street_Rod_AC.Parts.Export;

/// <summary>
/// The running gear of a car as Assetto Corsa physics data. The car's own figures are the factory parts',
/// whatever the units either side thinks in: what is mounted moves each figure by its ratio to the factory
/// part (a brake with twice the torque doubles the car's brake torque, a tyre with a fifth more grip lifts
/// the car's grip by a fifth). A car on its factory parts comes out byte for byte as its author left it.
/// </summary>
public static class AcRunningGearData
{
    private const string TyresFile = "tyres.ini";
    private const string BrakesFile = "brakes.ini";
    private const string SuspensionsFile = "suspensions.ini";
    private const string CarFile = "car.ini";

    /// <summary>The ratios of one axle: what is mounted over what came from the factory</summary>
    private sealed class AxleChange
    {
        public double TyreWidth = 1, TyreRadius = 1, RimRadius = 1, Grip = 1, LoadCapacity = 1, RollingResistance = 1, Pressure = 1;
        public double BrakeTorque = 1, SpringRate = 1, Bump = 1, Rebound = 1;
        public double TrackDelta, HubMassDelta, MassDelta;
    }

    /// <param name="mounted">What is on the car's wheel slots, by corner</param>
    /// <param name="factory">The parts the car's data describes</param>
    /// <param name="problems">What keeps the car from being driven: a corner without a wheel, brake, spring or shock</param>
    /// <returns>File name to new content, for the files that change</returns>
    public static Dictionary<string, string> Generate(PartsCatalog catalog, CornerParts[] mounted, (RunningGearFactory.AxleParts Front, RunningGearFactory.AxleParts Rear) factory,
        Func<string, string?> readFile, List<string> problems)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var front = Change(catalog, mounted.Take(2).ToList(), factory.Front, 0, problems);
        var rear = Change(catalog, mounted.Skip(2).Take(2).ToList(), factory.Rear, 2, problems);

        // A car on its factory parts changes nothing: only files with an edit are handed back
        void Keep(string name, IniText ini) { if (ini.Changed) files[name] = ini.ToString(); }

        if (readFile(TyresFile) is { } tyres) Keep(TyresFile, Tyres(new IniText(tyres), front, rear));
        if (readFile(BrakesFile) is { } brakes) Keep(BrakesFile, Brakes(new IniText(brakes), front, rear));
        if (readFile(SuspensionsFile) is { } suspensions) Keep(SuspensionsFile, Suspensions(new IniText(suspensions), front, rear));
        if (readFile(CarFile) is { } car && Math.Abs(front.MassDelta + rear.MassDelta) > 0.05) Keep(CarFile, Mass(new IniText(car), front.MassDelta + rear.MassDelta));

        return files;
    }

    private static AxleChange Change(PartsCatalog catalog, List<CornerParts> corners, RunningGearFactory.AxleParts factory, int firstCorner, List<string> problems)
    {
        var change = new AxleChange();
        var factoryBrake = RunningGear.BrakeTorque(factory.Brake);
        var factoryGrip = RunningGear.TyreGrip(factory.Tyre);
        var (factoryBump, factoryRebound) = RunningGear.Damping(factory.Shock);
        var factoryMass = factory.Rim.Mass + factory.Tyre.Mass + factory.Brake.Mass + factory.Spring.Mass + factory.Shock.Mass;

        double tyreWidth = 0, tyreRadius = 0, rimRadius = 0, grip = 0, load = 0, rolling = 0, pressure = 0;
        double brake = 0, spring = 0, bump = 0, rebound = 0, track = 0, hubMass = 0, mass = 0;

        for (var i = 0; i < corners.Count; i++)
        {
            var corner = corners[i];
            var name = RunningGear.CornerNames[firstCorner + i];

            var rim = Definition(catalog, corner.Rim);
            var tyre = Definition(catalog, corner.Tyre);
            var brakeDef = Definition(catalog, corner.Brake);
            var springDef = Definition(catalog, corner.Spring);
            var shockDef = Definition(catalog, corner.Shock);

            if (rim == null) problems.Add($"no wheel {name}");
            else if (tyre == null) problems.Add($"no tyre on the {name} wheel");
            if (brakeDef == null) problems.Add($"no brake {name}");
            if (springDef == null) problems.Add($"no spring {name}");
            if (shockDef == null) problems.Add($"no shock absorber {name}");

            // A corner that lacks a part is measured as the factory part, the problem says what is missing
            tyreWidth += Ratio(tyre == null ? 0 : RunningGear.TyreWidth(tyre), RunningGear.TyreWidth(factory.Tyre));
            tyreRadius += Ratio(tyre == null ? 0 : RunningGear.TyreRadius(tyre), RunningGear.TyreRadius(factory.Tyre));
            rimRadius += Ratio(tyre == null ? 0 : RunningGear.TyreRimRadius(tyre), RunningGear.TyreRimRadius(factory.Tyre));
            grip += Ratio(tyre == null ? 0 : RunningGear.TyreGrip(tyre, corner.Tyre!.Wear), factoryGrip);
            load += Ratio(tyre == null ? 0 : RunningGear.TyreLoadCapacity(tyre), RunningGear.TyreLoadCapacity(factory.Tyre));
            rolling += Ratio(tyre == null ? 0 : RunningGear.TyreRollingResistance(tyre), RunningGear.TyreRollingResistance(factory.Tyre));
            pressure += Ratio(tyre == null ? 0 : RunningGear.TyrePressure(tyre), RunningGear.TyrePressure(factory.Tyre));
            brake += Ratio(brakeDef == null ? 0 : RunningGear.BrakeTorque(brakeDef, corner.Brake!.Wear), factoryBrake);
            spring += Ratio(springDef == null ? 0 : RunningGear.SpringRate(springDef), RunningGear.SpringRate(factory.Spring));
            bump += Ratio(shockDef == null ? 0 : RunningGear.Damping(shockDef).Bump, factoryBump);
            rebound += Ratio(shockDef == null ? 0 : RunningGear.Damping(shockDef).Rebound, factoryRebound);

            // Offset is where the rim puts the wheel; negative moves it out, and the track grows by twice that
            if (rim != null) track += -2 * (RunningGear.RimOffset(rim) - RunningGear.RimOffset(factory.Rim));

            var unsprung = (rim?.Mass ?? factory.Rim.Mass) + (tyre?.Mass ?? factory.Tyre.Mass) + (brakeDef?.Mass ?? factory.Brake.Mass);
            hubMass += unsprung - (factory.Rim.Mass + factory.Tyre.Mass + factory.Brake.Mass);
            mass += unsprung + (springDef?.Mass ?? factory.Spring.Mass) + (shockDef?.Mass ?? factory.Shock.Mass) - factoryMass;
        }

        var n = Math.Max(1, corners.Count);
        change.TyreWidth = tyreWidth / n;
        change.TyreRadius = tyreRadius / n;
        change.RimRadius = rimRadius / n;
        change.Grip = grip / n;
        change.LoadCapacity = load / n;
        change.RollingResistance = rolling / n;
        change.Pressure = pressure / n;
        change.BrakeTorque = brake / n;
        change.SpringRate = spring / n;
        change.Bump = bump / n;
        change.Rebound = rebound / n;
        change.TrackDelta = track / n;
        change.HubMassDelta = hubMass / n;
        change.MassDelta = mass;
        return change;
    }

    private static PartDefinition? Definition(PartsCatalog catalog, PartInstance? part) => part == null ? null : catalog.Get(part.DefinitionId);

    /// <summary>Mounted over factory; a part without a figure, or none at all, counts as the factory part</summary>
    private static double Ratio(double mounted, double factory) => mounted > 0 && factory > 0 ? mounted / factory : 1;

    private static IniText Tyres(IniText ini, AxleChange front, AxleChange rear)
    {
        // Every compound the car offers: [FRONT], [FRONT_1]... and a second [FRONT] as some authors write it
        var sections = ini.Sections.ToList();
        var seen = new Dictionary<string, int>();
        foreach (var section in sections)
        {
            var occurrence = seen.GetValueOrDefault(section);
            seen[section] = occurrence + 1;

            var change = section.StartsWith("FRONT", StringComparison.Ordinal) ? front : section.StartsWith("REAR", StringComparison.Ordinal) ? rear : null;
            if (change == null || section.Contains("THERMAL", StringComparison.Ordinal)) continue;

            ini.Scale(section, "WIDTH", change.TyreWidth, "0.000", occurrence);
            ini.Scale(section, "RADIUS", change.TyreRadius, "0.0000", occurrence);
            ini.Scale(section, "RIM_RADIUS", change.RimRadius, "0.0000", occurrence);
            ini.Scale(section, "ANGULAR_INERTIA", change.TyreRadius * change.TyreRadius, "0.00", occurrence);
            foreach (var key in new[] { "DX_REF", "DY_REF", "DX0", "DY0" }) ini.Scale(section, key, change.Grip, "0.0000", occurrence);
            ini.Scale(section, "FZ0", change.LoadCapacity, "0", occurrence);
            ini.Scale(section, "ROLLING_RESISTANCE_0", change.RollingResistance, "0.00", occurrence);
            ini.Scale(section, "ROLLING_RESISTANCE_1", change.RollingResistance, "0.000000", occurrence);
            ini.Scale(section, "PRESSURE_STATIC", change.Pressure, "0", occurrence);
            ini.Scale(section, "PRESSURE_IDEAL", change.Pressure, "0", occurrence);
        }

        return ini;
    }

    private static IniText Brakes(IniText ini, AxleChange front, AxleChange rear)
    {
        if (Math.Abs(front.BrakeTorque - 1) < 1e-9 && Math.Abs(rear.BrakeTorque - 1) < 1e-9) return ini;

        var max = ini.GetNumber("DATA", "MAX_TORQUE") ?? 0;
        var share = ini.GetNumber("DATA", "FRONT_SHARE") ?? 0.6;
        var frontTorque = max * share * front.BrakeTorque;
        var rearTorque = max * (1 - share) * rear.BrakeTorque;

        ini.Set("DATA", "MAX_TORQUE", frontTorque + rearTorque, "0");
        if (frontTorque + rearTorque > 0) ini.Set("DATA", "FRONT_SHARE", frontTorque / (frontTorque + rearTorque), "0.000");
        ini.Scale("DATA", "HANDBRAKE_TORQUE", rear.BrakeTorque, "0");
        return ini;
    }

    private static IniText Suspensions(IniText ini, AxleChange front, AxleChange rear)
    {
        foreach (var (section, change) in new[] { ("FRONT", front), ("REAR", rear) })
        {
            if (!ini.Scale(section, "SPRING_RATE", change.SpringRate, "0")) ini.Scale(section + "_COILOVER_0", "RATE", change.SpringRate, "0");
            ini.Scale(section, "DAMP_BUMP", change.Bump, "0");
            ini.Scale(section, "DAMP_FAST_BUMP", change.Bump, "0");
            ini.Scale(section, "DAMP_REBOUND", change.Rebound, "0");
            ini.Scale(section, "DAMP_FAST_REBOUND", change.Rebound, "0");
            if (ini.GetNumber(section, "TRACK") is { } track && Math.Abs(change.TrackDelta) > 0.0005) ini.Set(section, "TRACK", track + change.TrackDelta, "0.000");
            if (ini.GetNumber(section, "HUB_MASS") is { } hub && Math.Abs(change.HubMassDelta) > 0.05) ini.Set(section, "HUB_MASS", Math.Max(hub * 0.5, hub + change.HubMassDelta), "0.0");
        }

        return ini;
    }

    private static IniText Mass(IniText ini, double delta)
    {
        if (ini.GetNumber("BASIC", "TOTALMASS") is { } mass) ini.Set("BASIC", "TOTALMASS", Math.Max(mass * 0.5, mass + delta), "0");
        return ini;
    }
}
