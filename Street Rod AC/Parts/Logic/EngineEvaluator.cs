using Street_Rod_AC.Parts.Scripting;

namespace Street_Rod_AC.Parts.Logic;

/// <summary>What an engine build comes to: whether it runs, its curve, and what it hands to the drivetrain</summary>
public sealed class EngineReport
{
    /// <summary>Why the engine does not run, in the part scripts' words; null when it does</summary>
    public string? Problem { get; init; }

    public bool Runs => Problem == null && Dyno is { MaxPowerHp: > 0 };

    public DynoInputs? Inputs { get; init; }
    public DynoResult? Dyno { get; init; }

    public double IdleRpm { get; init; }
    public double LimiterRpm { get; init; }

    /// <summary>Rotating inertia of the engine, kg m2</summary>
    public double Inertia { get; init; }

    /// <summary>Forward gear ratios, first gear first</summary>
    public IReadOnlyList<double> GearRatios { get; init; } = Array.Empty<double>();

    public double ReverseRatio { get; init; }
    public double FinalRatio { get; init; }

    /// <summary>1 = front, 2 = rear, 3 = all wheels; 0 when there is no transmission</summary>
    public int DriveType { get; init; }

    /// <summary>Limited slip lock, 0..1</summary>
    public double DiffLock { get; init; }

    /// <summary>Kilograms, all parts of the build</summary>
    public double Mass { get; init; }

    /// <summary>What the parts cost new</summary>
    public double Value { get; init; }

    public IReadOnlyList<PartDefinition> Unplaced { get; init; } = Array.Empty<PartDefinition>();
}

/// <summary>Runs an engine build through the part scripts and the dyno</summary>
public static class EngineEvaluator
{
    private const int ReverseGearIndex = 7;
    private const int MaxForwardGears = 6;

    public static EngineReport Evaluate(PartsCatalog catalog, PartTree tree)
    {
        var runtime = new PartScriptRuntime(catalog, tree.Root);
        runtime.UpdateCar();

        var block = runtime.ObjectOf(tree.Root);
        var chassis = runtime.Chassis;
        var data = block?.Fields.GetValueOrDefault("dynodata")?.AsObject;
        var dyno = data == null ? null : runtime.DynoResultOf(data);

        // The scripts' own verdict, asked after the dyno so the compression check has its figure. A part whose
        // script is not there looks like a missing part to the others; what they say about it would mislead.
        var problem = runtime.MissingScripts.Count > 0
            ? $"the script of {runtime.MissingScripts[0].Definition.Id} is missing ({runtime.MissingScripts.Count} in all): the parts need converting again."
            : runtime.Call(tree.Root, "isDynoable").AsText;
        if (problem == null && dyno == null) problem = "the engine could not be evaluated.";

        // The scripts hold the compression against what the fuel system allows. With no carburettor or injection
        // that limit is 0, and what they say ("too high, not more than 0.0:1") points away from what is wrong.
        var inputs = data == null ? null : DynoInputs.From(data);
        if (problem != null && inputs is { MaxFuelFlow: <= 0, MaxAirFlow: <= 0 } && problem.Contains("compression", StringComparison.OrdinalIgnoreCase))
            problem = "nothing feeds the engine: there is no carburettor or injection on the intake.";

        var gears = (int)(chassis?.Number("gears") ?? 0);
        var ratios = chassis?.Fields.GetValueOrDefault("ratio") as ScriptArray;
        double Ratio(int index) => ratios != null && ratios.Items.GetValueOrDefault(index) is ScriptNumber ratio ? ratio.Amount : 0;

        var parts = tree.Root.SelfAndDescendants().ToList();
        return new EngineReport
        {
            Problem = problem,
            Inputs = inputs,
            Dyno = dyno,
            IdleRpm = block?.Number("rpm_idle") ?? 0,
            LimiterRpm = block?.Number("RPM_limit") ?? 0,
            Inertia = block?.Number("inertia") ?? 0,
            GearRatios = Enumerable.Range(1, Math.Clamp(gears, 0, MaxForwardGears)).Select(Ratio).Where(r => r > 0).ToList(),
            ReverseRatio = Math.Abs(Ratio(ReverseGearIndex)),
            FinalRatio = chassis?.Number("rearend_ratio") ?? 0,
            DriveType = gears > 0 ? (int)(chassis?.Number("drive_type") ?? 0) : 0,
            DiffLock = chassis?.Number("diff_lock") ?? 0,
            Mass = parts.Sum(p => (double)p.Definition.Mass),
            Value = parts.Sum(p => p.Definition.Number("value")),
            Unplaced = tree.Unplaced
        };
    }
}
