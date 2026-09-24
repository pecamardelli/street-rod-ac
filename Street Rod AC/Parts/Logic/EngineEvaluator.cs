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

    /// <summary>
    /// The engine's own drag when it is pushed, the oil pan's figure over the losses of the moving parts,
    /// as the block leaves it on the car; 0 when there is no figure
    /// </summary>
    public double Friction { get; init; }

    /// <summary>The clutch's clamp figure (the script's maxF); 0 without a clutch</summary>
    public double ClutchCapacity { get; init; }

    /// <summary>Whether an exhaust-driven charger is on the engine: boost comes with a lag, and a gauge</summary>
    public bool Turbocharged { get; init; }

    /// <summary>Kilograms, all parts of the build</summary>
    public double Mass { get; init; }

    /// <summary>What the parts cost new</summary>
    public double Value { get; init; }

    public IReadOnlyList<PartDefinition> Unplaced { get; init; } = Array.Empty<PartDefinition>();

    /// <summary>
    /// What went wrong running the part scripts (a script that threw, one that ran out of steps), one line each,
    /// for the log; empty when they ran clean. Any of it makes the engine's <see cref="Problem"/>.
    /// </summary>
    public IReadOnlyList<string> ScriptFaults { get; init; } = Array.Empty<string>();
}

/// <summary>Runs an engine build through the part scripts and the dyno</summary>
public static class EngineEvaluator
{
    private const int ReverseGearIndex = 7;
    private const int MaxForwardGears = 6;

    /// <remarks>
    /// Never throws for what the content does: a part script that faults is a part left out, and the engine's
    /// problem says so (the scripts are third-party code, one bad class must not take every build down with it).
    /// </remarks>
    public static EngineReport Evaluate(PartsCatalog catalog, PartTree tree)
    {
        try
        {
            return Run(catalog, tree);
        }
        catch (Exception ex)
        {
            // Past the runtime's own containment (the figures themselves, a malformed tree): the engine does not run
            var fault = $"evaluating {tree.Root.Definition.Id} failed ({ex.GetType().Name}: {ex.Message})";
            return new EngineReport { Problem = "the engine could not be evaluated: " + fault, ScriptFaults = new[] { fault }, Unplaced = tree.Unplaced };
        }
    }

    private static EngineReport Run(PartsCatalog catalog, PartTree tree)
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

        // A script that threw or ran out of steps left figures half made: whatever they come to is not the engine
        var faults = runtime.Faults.ToList();
        if (runtime.BudgetExhausted)
            faults.Add($"a part script ran out of steps in {runtime.BudgetExhaustedIn ?? "an unknown method"} (it loops, or does far more than a part should)");
        if (faults.Count > 0 && runtime.MissingScripts.Count == 0)
            problem = $"a part script failed: {faults[0]}{(faults.Count > 1 ? $" ({faults.Count} faults in all)" : "")}.";

        if (problem == null && dyno == null) problem = "the engine could not be evaluated.";

        // The scripts hold the compression against what the fuel system allows. With no carburettor or injection
        // that limit is 0, and what they say ("too high, not more than 0.0:1") points away from what is wrong.
        var inputs = data == null ? null : DynoInputs.From(data);
        if (problem != null && inputs is { MaxFuelFlow: <= 0, MaxAirFlow: <= 0 } && problem.Contains("compression", StringComparison.OrdinalIgnoreCase))
            problem = "nothing feeds the engine: there is no carburettor or injection on the intake.";

        // Figures come out of script math (a square root of a negative, a float overflow) and pack.json: what is not
        // a number is no figure, so nothing that is not one ever reaches the car's data
        var gears = (int)Finite(chassis?.Number("gears"));
        var ratios = chassis?.Fields.GetValueOrDefault("ratio") as ScriptArray;
        double Ratio(int index) => ratios != null && ratios.Items.GetValueOrDefault(index) is ScriptNumber ratio ? Finite(ratio.Amount) : 0;

        var parts = tree.Root.SelfAndDescendants().ToList();
        var clutch = parts.FirstOrDefault(p => p.Is("Clutch"));
        return new EngineReport
        {
            Problem = problem,
            Inputs = inputs,
            Dyno = dyno,
            IdleRpm = Finite(block?.Number("rpm_idle")),
            LimiterRpm = Finite(block?.Number("RPM_limit")),
            Inertia = Finite(block?.Number("inertia")),
            GearRatios = Enumerable.Range(1, Math.Clamp(gears, 0, MaxForwardGears)).Select(Ratio).Where(r => r > 0).ToList(),
            ReverseRatio = Math.Abs(Ratio(ReverseGearIndex)),
            FinalRatio = Finite(chassis?.Number("rearend_ratio")),
            DriveType = gears > 0 ? (int)Finite(chassis?.Number("drive_type")) : 0,
            DiffLock = Finite(chassis?.Number("diff_lock")),
            Friction = Finite(chassis?.Number("engine_friction_fwd")),
            ClutchCapacity = clutch == null ? 0 : Finite(runtime.ObjectOf(clutch)?.Number("maxF") ?? clutch.Definition.Number("maxF")),
            Turbocharged = parts.Any(p => p.Is("TurboCharger")),
            Mass = Finite(parts.Sum(p => (double)p.Definition.Mass)),
            Value = Finite(parts.Sum(p => p.Definition.Number("value"))),
            ScriptFaults = faults,
            Unplaced = tree.Unplaced
        };
    }

    /// <summary>A figure as the report may carry it: missing, NaN and infinities are 0 (no figure)</summary>
    private static double Finite(double? value) => value is { } number && double.IsFinite(number) ? number : 0;
}
