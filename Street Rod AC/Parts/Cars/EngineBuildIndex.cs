using Street_Rod_AC.Parts.Logic;

namespace Street_Rod_AC.Parts.Cars;

/// <summary>An engine build that assembles and runs, with what it makes on the dyno</summary>
public sealed record RatedBuild(EngineBuild Build, string BlockId, string Family, double PowerHp, double TorqueNm, double Litres, double MassKg)
{
    /// <summary>Words of the build's name, for matching it to a car: "chevrolet", "camaro", "396"</summary>
    public IReadOnlySet<string> NameTokens { get; } = MakeFamilies.Tokens(Build.Name);
}

/// <summary>
/// Every engine build of the catalog put on the dyno once. Two hundred builds take about a second,
/// so this is made when it is first needed and kept for the session.
/// </summary>
public sealed class EngineBuildIndex
{
    private readonly Dictionary<string, RatedBuild> _byId = new(StringComparer.OrdinalIgnoreCase);

    private EngineBuildIndex() { }

    /// <summary>No builds at all: what there is when the catalog has none, or they could not be rated</summary>
    public static EngineBuildIndex Empty { get; } = new();

    /// <summary>Builds that run, in the order of the catalog</summary>
    public IReadOnlyList<RatedBuild> Runnable { get; private set; } = Array.Empty<RatedBuild>();

    /// <summary>Builds whose part scripts faulted (or that could not be rated at all) while they were rated, one line each, for the log</summary>
    public IReadOnlyList<string> ScriptFaults { get; private set; } = Array.Empty<string>();

    public RatedBuild? Get(string? buildId) => buildId == null ? null : _byId.GetValueOrDefault(buildId);

    public static EngineBuildIndex Create(PartsCatalog catalog)
    {
        var evaluated = new List<(EngineBuild Build, string BlockId, EngineReport Report)>();
        var faults = new List<string>();
        foreach (var build in catalog.EngineBuilds)
        {
            // One build that breaks the assembler or the dyno (content edited by hand) is one fault, not a
            // session without builds: every car would come without its engine
            try
            {
                var tree = PartTreeBuilder.BuildEngine(catalog, build);
                if (tree == null) continue;

                // A build whose scripts fault does not run; the others are rated all the same
                var report = EngineEvaluator.Evaluate(catalog, tree);
                faults.AddRange(report.ScriptFaults.Select(fault => $"{build.Id}: {fault}"));
                if (report.Runs) evaluated.Add((build, tree.Root.Definition.Id, report));
            }
            catch (Exception ex)
            {
                faults.Add($"{build.Id}: {ex.GetType().Name}: {ex.Message}; left out");
            }
        }

        // An engine belongs to the make whose cars it came in; builds from notes are named after the make
        var familyOfBlock = evaluated
            .GroupBy(e => e.BlockId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => MakeFamilies.FamilyOf(e.Build.Name)).Where(f => f.Length > 0)
                    .GroupBy(f => f).OrderByDescending(f => f.Count()).Select(f => f.Key).FirstOrDefault() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

        var index = new EngineBuildIndex();
        var runnable = new List<RatedBuild>();
        foreach (var (build, blockId, report) in evaluated)
        {
            var dyno = report.Dyno!;
            var rated = new RatedBuild(build, blockId, familyOfBlock[blockId], dyno.MaxPowerHp, dyno.MaxTorque, dyno.Displacement * 1000, report.Mass);
            runnable.Add(rated);
            index._byId[build.Id] = rated;
        }

        index.Runnable = runnable;
        index.ScriptFaults = faults;
        return index;
    }
}
