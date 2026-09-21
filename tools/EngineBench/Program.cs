using System.Globalization;
using Street_Rod_AC.Parts;
using Street_Rod_AC.Parts.Logic;

namespace Street_Rod_AC;

/// <summary>
/// Puts engine builds of the converted parts on the dyno from the command line:
/// lists them, shows one in detail, or checks all builds with a known power against the engine model.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        if (args.Length < 2)
        {
            Console.WriteLine("Usage: EngineBench <parts folder> list | rated | all | show <build id>");
            return 1;
        }

        var catalog = PartsCatalog.Load(args[0]);
        Console.WriteLine($"{catalog.Parts.Count} parts, {catalog.EngineBuilds.Count} engine builds");

        switch (args[1])
        {
            case "list":
                foreach (var build in catalog.EngineBuilds) Console.WriteLine($"  {build.Id,-62} {build.Name}");
                return 0;

            case "rated":
                return Table(catalog, catalog.EngineBuilds.Where(b => b.RatedPower != null));

            case "all":
                return Table(catalog, catalog.EngineBuilds);

            case "inputs":
                return Inputs(catalog, catalog.EngineBuilds.Where(b => b.RatedPower != null));

            case "show" when args.Length > 2:
                return Show(catalog, args[2]);

            default:
                Console.WriteLine("Unknown command");
                return 1;
        }
    }

    private static int Table(PartsCatalog catalog, IEnumerable<EngineBuild> builds)
    {
        Console.WriteLine($"{"build",-48} {"rated",6} {"model",6} {"error",6}  {"@rpm",5} {"Nm",5} {"@rpm",5} {"litres",6} {"CR",5}  problem");
        var errors = new List<double>();
        foreach (var build in builds)
        {
            var tree = PartTreeBuilder.BuildEngine(catalog, build);
            if (tree == null)
            {
                Console.WriteLine($"{Short(build.Id),-48} no engine block among its parts");
                continue;
            }

            var report = EngineEvaluator.Evaluate(catalog, tree);
            var dyno = report.Dyno;
            var error = build.RatedPower is { } rated && dyno != null ? (dyno.MaxPowerHp - rated) / rated : (double?)null;
            if (error != null && report.Problem == null) errors.Add(error.Value);

            Console.WriteLine($"{Short(build.Id),-48} {build.RatedPower,6:0} {dyno?.MaxPowerHp,6:0} {error,6:+0%;-0%}  {dyno?.MaxPowerRpm,5:0} " +
                              $"{dyno?.MaxTorque,5:0} {dyno?.MaxTorqueRpm,5:0} {dyno?.Displacement * 1000,6:0.00} {dyno?.Compression,5:0.0}  " +
                              $"{report.Problem}{(tree.Unplaced.Count > 0 ? $" [{tree.Unplaced.Count} unplaced]" : "")}");
        }

        if (errors.Count > 0)
            Console.WriteLine($"\n{errors.Count} rated builds that run: mean error {errors.Average():+0.0%;-0.0%}, " +
                              $"mean absolute {errors.Average(Math.Abs):0.0%}, worst {errors.MaxBy(Math.Abs):+0.0%;-0.0%}");
        return 0;
    }

    /// <summary>What the scripts feed the engine model, side by side</summary>
    private static int Inputs(PartsCatalog catalog, IEnumerable<EngineBuild> builds)
    {
        Console.WriteLine($"{"build",-30} {"hp",4} {"inMax",6} {"inMin",6} {"exMax",6} {"inDur",5} {"exDur",5} {"inOpn",5} {"exOpn",5} {"lead0",5} {"lead1",5} {"burn ms",7} {"Tloss",5} {"A/F",5} {"H MJ",5} {"air",6} {"fuel",6} {"maxRpm",6} {"boost",5}");
        foreach (var build in builds)
        {
            var tree = PartTreeBuilder.BuildEngine(catalog, build);
            var i = tree == null ? null : EngineEvaluator.Evaluate(catalog, tree).Inputs;
            if (i == null) continue;

            // BENCH_JSON=<file>: one JSON object per build, for fitting the engine model outside
            if (Environment.GetEnvironmentVariable("BENCH_JSON") is { } jsonFile)
                File.AppendAllText(jsonFile, Newtonsoft.Json.JsonConvert.SerializeObject(new { build.Id, build.RatedPower, Inputs = i }) + Environment.NewLine);

            double Span(double open, double close) => (close - open < 0 ? close - open + 1 : close - open) * 720;
            Console.WriteLine($"{Short(build.Id)[^Math.Min(30, Short(build.Id).Length)..],-30} {build.RatedPower,4:0} {i.IntakeMax,6:0.000} {i.IntakeMin,6:0.000} {i.ExhaustMax,6:0.000} " +
                              $"{Span(i.IntakeOpen, i.IntakeClose),5:0} {Span(i.ExhaustOpen, i.ExhaustClose),5:0} {i.IntakeOpen * 720,5:0} {i.ExhaustOpen * 720,5:0} " +
                              $"{(0.5 - i.SparkMin) * 720,5:0} {(0.5 - i.SparkMin - i.SparkInc) * 720,5:0} {i.BurnTime * 1000,7:0.000} {i.HeatLoss,5:0} {i.MixtureRatio,5:0.0} " +
                              $"{i.MixtureHeat / 1e6,5:0.00} {i.MaxAirFlow,6:0.000} {i.MaxFuelFlow,6:0.000} {i.MaxRpm,6:0} {i.BoostMax,5:0.00}");
        }

        return 0;
    }

    private static int Show(PartsCatalog catalog, string id)
    {
        var build = catalog.EngineBuilds.FirstOrDefault(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                    ?? catalog.EngineBuilds.FirstOrDefault(b => b.Id.Contains(id, StringComparison.OrdinalIgnoreCase));
        if (build == null)
        {
            Console.WriteLine($"No build '{id}'");
            return 1;
        }

        var tree = PartTreeBuilder.BuildEngine(catalog, build);
        if (tree == null)
        {
            Console.WriteLine("No engine block among its parts");
            return 1;
        }

        Console.WriteLine($"\n{build.Name}  ({build.Id})");
        Print(tree.Root, 0);
        foreach (var part in tree.Unplaced) Console.WriteLine($"  UNPLACED {part.Id}");
        foreach (var missing in tree.Missing) Console.WriteLine($"  MISSING  {missing}");

        var report = EngineEvaluator.Evaluate(catalog, tree);
        Console.WriteLine($"\nRuns: {report.Runs}{(report.Problem != null ? " - " + report.Problem : "")}");
        Console.WriteLine($"Inputs: {report.Inputs}");
        Console.WriteLine($"Idle {report.IdleRpm:0} rpm, limiter {report.LimiterRpm:0} rpm, inertia {report.Inertia:0.000} kg m2, " +
                          $"mass {report.Mass:0} kg, value ${report.Value:0}");
        Console.WriteLine($"Gears {string.Join(" / ", report.GearRatios.Select(r => r.ToString("0.00")))}  reverse {report.ReverseRatio:0.00}  " +
                          $"final {report.FinalRatio:0.00}  drive {report.DriveType}  lock {report.DiffLock:0.00}");

        if (report.Dyno is { } dyno)
        {
            Console.WriteLine($"{dyno.Displacement * 1000:0.00} l, {dyno.Compression:0.0}:1, {dyno.MaxPowerHp:0} hp @ {dyno.MaxPowerRpm:0}, " +
                              $"{dyno.MaxTorque:0} Nm @ {dyno.MaxTorqueRpm:0}{(build.RatedPower != null ? $"  (rated {build.RatedPower:0} hp)" : "")}");
            foreach (var (rpm, torque) in dyno.Curve.Where(p => p.Rpm % 500 == 0))
                Console.WriteLine($"  {rpm,6:0} rpm {torque,6:0} Nm {dyno.PowerHpAt(rpm),6:0} hp  {new string('#', (int)(torque / 12))}");
        }

        return 0;
    }

    private static void Print(InstalledPart part, int depth)
    {
        Console.WriteLine($"  {new string(' ', depth * 2)}{(depth == 0 ? "" : $"[{part.ParentSlot}] ")}{part}");
        foreach (var child in part.Children.OrderBy(c => c.Key)) Print(child.Value, depth + 1);
    }

    private static string Short(string id) => id.Length > 48 ? id[^48..] : id;
}
