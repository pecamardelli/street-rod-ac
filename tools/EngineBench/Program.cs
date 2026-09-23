using System.Globalization;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Parts.Export;
using Street_Rod_AC.Parts.Logic;
using Street_Rod_AC.Parts.Sounds;

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
            Console.WriteLine("Usage: EngineBench <parts folder> list | rated | all | inputs | show <build id>");
            Console.WriteLine("       EngineBench <parts folder> export <build id> <car data folder> <output folder>");
            Console.WriteLine("       EngineBench <parts folder> cars <AC cars folder>       factory engine suggested for every car");
            Console.WriteLine("       EngineBench <parts folder> tune <build id> [count]     engines a used car of that build may turn up with");
            Console.WriteLine("       EngineBench <parts folder> bench <build id>            take every part off and find where it goes back");
            Console.WriteLine("       EngineBench <parts folder> renew <older parts folder>  engines saved with an older conversion, brought up to date");
            Console.WriteLine("       EngineBench <parts folder> gear <AC cars folder>       factory running gear chosen for every car");
            Console.WriteLine("       EngineBench <parts folder> car <AC car folder> <build id> [output folder]   every data file the car's parts change");
            Console.WriteLine("       EngineBench <parts folder> sound <AC car folder> <car id> [output folder]   the car's sound written for another car id");
            Console.WriteLine("       EngineBench <parts folder> sounds <AC cars folder> [sounds folder]        the sound library, and the sound every build gets");
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

            case "export" when args.Length > 4:
                return Export(catalog, args[2], args[3], args[4]);

            case "cars" when args.Length > 2:
                return Cars(catalog, args[2]);

            case "tune" when args.Length > 2:
                return Tune(catalog, args[2], args.Length > 3 ? int.Parse(args[3]) : 10);

            case "bench" when args.Length > 2:
                return Bench(catalog, args[2]);

            case "renew" when args.Length > 2:
                return Renew(catalog, args[2]);

            case "gear" when args.Length > 2:
                return Gear(catalog, args[2]);

            case "car" when args.Length > 3:
                return Car(catalog, args[2], args[3], args.Length > 4 ? args[4] : null);

            case "sound" when args.Length > 3:
                return Sound(args[2], args[3], args.Length > 4 ? args[4] : null);

            case "sounds" when args.Length > 2:
                return Sounds(catalog, args[2], args.Length > 3 ? args[3] : Path.Combine(args[0], "..", "Sounds"));

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
                          $"mass {report.Mass:0} kg, value ${report.Value:0}, friction {report.Friction:0.000000}, clutch {report.ClutchCapacity:0}{(report.Turbocharged ? ", turbo" : "")}");
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

    /// <summary>Writes the Assetto Corsa data files a build changes, made from an unpacked car data folder</summary>
    private static int Export(PartsCatalog catalog, string id, string carData, string output)
    {
        var build = catalog.EngineBuilds.FirstOrDefault(b => b.Id.Contains(id, StringComparison.OrdinalIgnoreCase));
        var tree = build == null ? null : PartTreeBuilder.BuildEngine(catalog, build);
        if (build == null || tree == null)
        {
            Console.WriteLine($"No usable build '{id}'");
            return 1;
        }

        var report = EngineEvaluator.Evaluate(catalog, tree);
        if (!report.Runs)
        {
            Console.WriteLine($"{build.Name} does not run: {report.Problem}");
            return 1;
        }

        Dictionary<string, string> files;
        try
        {
            files = AcEngineData.Generate(report, name => File.Exists(Path.Combine(carData, name)) ? File.ReadAllText(Path.Combine(carData, name)) : null);
        }
        catch (FileNotFoundException ex)
        {
            Console.WriteLine($"{ex.Message}: {carData} is not the data folder of a car");
            return 1;
        }

        Directory.CreateDirectory(output);
        foreach (var (name, content) in files) File.WriteAllText(Path.Combine(output, name), content);

        Console.WriteLine($"{build.Name}: {report.Dyno!.MaxPowerHp:0} hp, {report.GearRatios.Count} gears -> {string.Join(", ", files.Keys)} in {output}");
        return 0;
    }

    /// <summary>The engine each Assetto Corsa car would get, with the runners-up</summary>
    private static int Cars(PartsCatalog catalog, string carsFolder)
    {
        var index = EngineBuildIndex.Create(catalog);
        Console.WriteLine($"{index.Runnable.Count} builds run\n");

        foreach (var folder in Directory.GetDirectories(carsFolder))
        {
            var uiFile = Path.Combine(folder, "ui", "ui_car.json");
            if (!File.Exists(uiFile)) continue;

            var ui = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(uiFile));
            var brand = (string?)ui["brand"] ?? "";
            var name = (string?)ui["name"] ?? "";
            var bhp = StockEngineMatcher.ParsePower((string?)ui["specs"]?["bhp"]);

            Console.WriteLine($"{Path.GetFileName(folder)}: {brand} | {name} | {bhp:0} bhp  (family '{MakeFamilies.FamilyOf(brand + " " + name)}')");
            foreach (var match in StockEngineMatcher.Rank(index, brand, name, bhp).Take(4))
                Console.WriteLine($"    {match.Score,6:0.00}  {match.Build.PowerHp,4:0} hp {match.Build.Litres,5:0.00} l  [{match.Build.Family,-6}] {match.Build.Build.Name}  ({match.Build.Build.Id})");
        }

        return 0;
    }

    private static int Tune(PartsCatalog catalog, string id, int count)
    {
        var index = EngineBuildIndex.Create(catalog);
        var stock = index.Runnable.FirstOrDefault(b => b.Build.Id.Contains(id, StringComparison.OrdinalIgnoreCase));
        if (stock == null)
        {
            Console.WriteLine($"No build '{id}' that runs");
            return 1;
        }

        Console.WriteLine($"{stock.Build.Name}: {stock.PowerHp:0} hp, family '{stock.Family}'\n");
        var random = new Random(1);
        var started = DateTime.Now;
        for (var i = 0; i < count; i++)
        {
            var level = random.NextDouble();
            var engine = EngineFactory.CreateTuned(catalog, index, stock, 0.8, level, random);
            if (engine == null) continue;

            Console.WriteLine($"  level {level:0.00}: {engine.Report.Dyno?.MaxPowerHp,4:0} hp  ${PartPricing.WorthOfAssembly(catalog, engine.Root),6:0}  " +
                              $"{(engine.IsModified ? string.Join(", ", engine.Changes) : "stock")}{(engine.Report.Runs ? "" : "  DOES NOT RUN: " + engine.Report.Problem)}");
        }

        Console.WriteLine($"\n{(DateTime.Now - started).TotalMilliseconds / count:0} ms per engine");
        return 0;
    }

    /// <summary>Every part of a build comes off and has to find its way back to where it was</summary>
    private static int Bench(PartsCatalog catalog, string id)
    {
        var index = EngineBuildIndex.Create(catalog);
        var stock = index.Runnable.FirstOrDefault(b => b.Build.Id.Contains(id, StringComparison.OrdinalIgnoreCase));
        var engine = stock == null ? null : EngineFactory.CreateStock(catalog, stock, 1.0, new Random(1));
        if (engine == null)
        {
            Console.WriteLine($"No build '{id}' that runs");
            return 1;
        }

        var car = new List<PartInstance> { engine.Root };
        var failures = 0;
        foreach (var part in engine.Root.SelfAndDescendants().Skip(1).ToList())
        {
            var parent = PartTrees.FindParent(engine.Root, part)!;
            var (slot, own) = (part.ParentSlot, part.OwnSlot);

            Workbench.Remove(car, part);
            var without = EngineFactory.Evaluate(catalog, engine.Root);
            var places = Workbench.FindPlaces(catalog, car, part);
            var back = places.FirstOrDefault(p => ReferenceEquals(p.Parent, parent) && p.ParentSlot == slot);
            var definition = catalog.Get(part.DefinitionId)!;
            var parentDefinition = catalog.Get(parent.DefinitionId)!;
            var alternatives = catalog.FindMountable(parentDefinition, parentDefinition.Slots.First(s => s.Id == slot));

            Console.WriteLine($"  {(back == null ? "LOST" : "ok  ")} {definition.DisplayName ?? definition.Id,-46} {places.Count} place(s), " +
                              $"{alternatives.Count} part(s) fit its slot{(alternatives.Any(a => a.Part == definition) ? "" : "  (not itself!)")}");
            Console.WriteLine($"         without it: {(without is { Runs: true } ? $"{without.Dyno!.MaxPowerHp:0} hp" : without?.Problem)}  (CR {without?.Dyno?.Compression:0.0})");
            if (back == null) failures++;

            Workbench.Mount(car, back ?? new MountPlace(parent, slot, own), part);
        }

        var after = EngineFactory.Evaluate(catalog, engine.Root);
        Console.WriteLine($"\n{failures} part(s) could not go back; engine makes {after?.Dyno?.MaxPowerHp:0} hp (was {engine.Report.Dyno?.MaxPowerHp:0})");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// What a save made with an older conversion goes through: every engine of the old catalog that holds parts
    /// the current one knows by another id is brought up to date and has to run again with all its parts on.
    /// </summary>
    private static int Renew(PartsCatalog catalog, string oldPartsFolder)
    {
        var old = PartsCatalog.Load(oldPartsFolder);
        var failures = 0;
        var renewed = 0;

        foreach (var stock in EngineBuildIndex.Create(old).Runnable)
        {
            var engine = EngineFactory.CreateStock(old, stock, 1.0, new Random(1));
            if (engine == null) continue;

            var parts = engine.Root.SelfAndDescendants().Count();
            var loose = new List<PartInstance>();
            if (!SavedParts.BringUpToDate(catalog, engine.Root, loose)) continue;

            renewed++;
            var gone = engine.Root.SelfAndDescendants().Concat(loose.SelectMany(l => l.SelfAndDescendants())).Count(p => catalog.Get(p.DefinitionId) == null);
            var after = EngineFactory.Evaluate(catalog, engine.Root);
            var runs = after is { Runs: true };
            if (!runs || gone > 0) failures++;

            Console.WriteLine($"  {(runs && gone == 0 ? "ok  " : "FAIL")} {Short(stock.Build.Id),-48} {stock.PowerHp,4:0} hp -> " +
                              $"{(runs ? $"{after!.Dyno!.MaxPowerHp,4:0} hp" : after?.Problem ?? "no engine")}  {parts} parts, {loose.Count} came off, {gone} unknown");
            foreach (var part in loose) Console.WriteLine($"         off: {catalog.Get(part.DefinitionId)?.DisplayName ?? part.DefinitionId}");
        }

        Console.WriteLine($"\n{renewed} engine(s) brought up to date, {failures} with problems");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>The running gear every car leaves the factory with, matched to its own data</summary>
    private static int Gear(PartsCatalog catalog, string carsFolder)
    {
        foreach (var folder in Directory.GetDirectories(carsFolder))
        {
            if (!File.Exists(Path.Combine(folder, "ui", "ui_car.json"))) continue;

            AcCarSpecs specs;
            try { specs = AcCarSpecs.Read(AcCarDataReader.ForCar(folder)); }
            catch (Exception ex) { Console.WriteLine($"{Path.GetFileName(folder)}: {ex.Message}"); continue; }

            Console.WriteLine($"{Path.GetFileName(folder)}: {specs.TotalMass:0} kg, {specs.FrontWeightShare:0%} front");
            if (RunningGearFactory.Choose(catalog, specs) is not var (front, rear))
            {
                Console.WriteLine("    no running gear in the catalog");
                continue;
            }

            foreach (var (axle, parts, name) in new[] { (specs.Front, front, "front"), (specs.Rear, rear, "rear") })
            {
                Console.WriteLine($"  {name}: tyre {axle.TyreWidth * 1000:0}/{axle.TyreRadius:0.000}/{axle.RimRadius:0.000} m, brake {axle.BrakeTorque:0} Nm, spring {axle.SpringRate:0} N/m, damp {axle.DampBump:0}/{axle.DampRebound:0}, {axle.CornerLoad:0} kg/wheel");
                Console.WriteLine($"    tyre   {RunningGear.TyreWidth(parts.Tyre) * 1000:0}/{RunningGear.TyreRadius(parts.Tyre):0.000}/{RunningGear.TyreRimRadius(parts.Tyre):0.000} m  grip {RunningGear.TyreGrip(parts.Tyre):0.00}  {parts.Tyre.Id}");
                Console.WriteLine($"    rim    {RunningGear.RimWidth(parts.Rim):0.0}\" offset {RunningGear.RimOffset(parts.Rim) * 1000:0} mm, {parts.Rim.Mass:0.0} kg  {parts.Rim.Id}");
                Console.WriteLine($"    brake  {RunningGear.BrakeTorque(parts.Brake):0} Nm  {parts.Brake.DisplayName ?? parts.Brake.Id}");
                Console.WriteLine($"    spring {RunningGear.SpringRate(parts.Spring):0} N/m for {RunningGear.SpringDesignLoad(parts.Spring):0} kg  {parts.Spring.DisplayName ?? parts.Spring.Id}");
                Console.WriteLine($"    shock  {RunningGear.Damping(parts.Shock).Bump:0}/{RunningGear.Damping(parts.Shock).Rebound:0}  {parts.Shock.DisplayName ?? parts.Shock.Id}");
            }
        }

        return 0;
    }

    /// <summary>Every data file a car's parts change: the build as its engine, the factory running gear as its wheels</summary>
    /// <summary>
    /// A donor car's sound as the overlay would write it under another car's id: what the GUIDs keep, what they
    /// drop, and which engine events the donor lacks. The master GUIDs next to the cars folder serve a Kunos donor.
    /// </summary>
    private static int Sound(string donorFolder, string carId, string? output)
    {
        var master = Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(donorFolder)) ?? "", "..", "sfx", AcCarSound.GuidsFileName);
        var sound = AcCarSound.FromCar(donorFolder, Path.GetFullPath(master));
        if (sound == null)
        {
            Console.WriteLine($"{donorFolder} has no bank of its own, or no GUIDs to go with it");
            return 1;
        }

        var guids = sound.GuidsFor(carId);
        var before = sound.GuidsText.Split('\n').Count(l => l.Contains('}'));
        var after = guids.Split('\n').Count(l => l.Contains('}'));
        Console.WriteLine($"{sound.DonorId}: {new FileInfo(sound.BankPath).Length / 1e6:0.0} MB bank, {before} GUID line(s) -> {after} for {carId}");
        foreach (var line in guids.Split('\n').Where(l => l.Contains("event:/cars/") || l.Contains("bank:/"))) Console.WriteLine("  " + line.TrimEnd());
        var missing = AcCarSound.MissingEngineEvents(guids, carId);
        if (missing.Count > 0) Console.WriteLine($"  MISSING {string.Join(", ", missing)}");

        if (output != null)
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, AcCarSound.GuidsFileName), guids);
            Console.WriteLine($"  written to {Path.Combine(output, AcCarSound.GuidsFileName)}");
        }

        return missing.Count == 0 ? 0 : 2;
    }

    /// <summary>
    /// The sound library as the game sees it: the curated sounds, the banks harvested off the installed cars
    /// (one line per distinct bank, with its carriers), then the sound every runnable build would race with.
    /// </summary>
    private static int Sounds(PartsCatalog catalog, string carsFolder, string soundsFolder)
    {
        var index = EngineBuildIndex.Create(catalog);
        var library = SoundLibrary.Load(Path.GetFullPath(soundsFolder));
        var master = Path.GetFullPath(Path.Combine(carsFolder, "..", "sfx", AcCarSound.GuidsFileName));
        var started = DateTime.Now;
        var cars = library.Harvest(carsFolder, master, SoundLibrary.StockFacts(index, catalog));
        Console.WriteLine($"{library.Curated.Count} curated sound(s) under {library.Root}, {library.Harvested.Count} harvested off {cars} car(s) in {(DateTime.Now - started).TotalMilliseconds:0} ms\n");

        Console.WriteLine($"{"sound",-44} {"MB",5} {"cyl",3} {"family",-6} {"rpm",5} {"cars",4}  name / carriers");
        foreach (var sound in library.All.OrderByDescending(e => e.Curated).ThenBy(e => e.Id, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"{Short(sound.Id),-44} {sound.Size / 1e6,5:0} {sound.Cylinders,3} {sound.Family,-6} {sound.RpmMax,5:0} {sound.Carriers.Count,4}  {sound.Name}" +
                              (sound.Carriers.Count > 1 ? $"  [{string.Join(", ", sound.Carriers.Take(4))}{(sound.Carriers.Count > 4 ? ", ..." : "")}]" : ""));
        }

        Console.WriteLine($"\n{"build",-48} {"cyl",3} {"family",-6} {"limit",5}  sound: reason");
        foreach (var build in index.Runnable)
        {
            var tree = PartTreeBuilder.BuildEngine(catalog, build.Build);
            if (tree == null) continue;
            var report = EngineEvaluator.Evaluate(catalog, tree);
            var request = SoundMatcher.RequestFor(catalog, tree.Root.Definition.Id, report);
            var choice = SoundMatcher.Choose(library, request);
            Console.WriteLine($"{Short(build.Build.Id),-48} {request.Cylinders,3} {request.Family,-6} {request.LimiterRpm,5:0}  " +
                              (choice == null ? "none" : $"{choice.Sound.Name} ({Short(choice.Sound.Id)}) [{choice.Score:0.0}]: {choice.Reason}"));
        }

        return 0;
    }

    private static int Car(PartsCatalog catalog, string carFolder, string id, string? output)
    {
        var build = catalog.EngineBuilds.FirstOrDefault(b => b.Id.Contains(id, StringComparison.OrdinalIgnoreCase));
        var tree = build == null ? null : PartTreeBuilder.BuildEngine(catalog, build);
        if (build == null || tree == null)
        {
            Console.WriteLine($"No usable build '{id}'");
            return 1;
        }

        var readFile = AcCarDataReader.ForCar(carFolder);
        var specs = AcCarSpecs.Read(readFile);
        var factory = RunningGearFactory.Choose(catalog, specs);
        var mounted = RunningGear.Mounted(RunningGearFactory.Create(catalog, specs, 1));

        var report = EngineEvaluator.Evaluate(catalog, tree);
        var result = AcCarBuild.Generate(catalog, new CarBuild
        {
            Engine = report,
            FactoryEngineMass = report.Mass,
            RunningGear = mounted,
            FactoryRunningGear = factory
        }, readFile);

        Console.WriteLine($"{build.Name}: {report.Dyno?.MaxPowerHp:0} hp -> {string.Join(", ", result.Files.Keys)}");
        foreach (var problem in result.Problems) Console.WriteLine($"  PROBLEM {problem}");

        // The sound the car would race with, from the library next to the parts and the cars next to this one
        var carsFolder = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(carFolder)) ?? carFolder;
        var library = SoundLibrary.Load(Path.GetFullPath(Path.Combine(catalog.Root, "..", "Sounds")));
        library.Harvest(carsFolder, Path.GetFullPath(Path.Combine(carsFolder, "..", "sfx", AcCarSound.GuidsFileName)), SoundLibrary.StockFacts(EngineBuildIndex.Create(catalog), catalog));
        var carId = Path.GetFileName(Path.TrimEndingDirectorySeparator(carFolder));
        var sound = SoundMatcher.ForCar(library, SoundMatcher.RequestFor(catalog, tree.Root.Definition.Id, report), carId, out var choice);
        if (choice != null) Console.WriteLine($"  sound: {choice.Sound.Name} ({choice.Sound.Id}): {choice.Reason}{(sound == null ? " (the car's own bank)" : "")}");

        // Only what changed, line by line
        foreach (var (name, content) in result.Files)
        {
            var before = (readFile(name) ?? "").Replace("\r\n", "\n").Split('\n');
            var after = content.Replace("\r\n", "\n").Split('\n');
            var changed = after.Where((line, i) => i >= before.Length || before[i] != line).Count(l => l.Length > 0);
            Console.WriteLine($"  {name}: {changed} line(s) differ");
        }

        if (output != null)
        {
            Directory.CreateDirectory(output);
            foreach (var (name, content) in result.Files) File.WriteAllText(Path.Combine(output, name), content);
        }

        return result.CanDrive ? 0 : 1;
    }

    private static void Print(InstalledPart part, int depth)
    {
        Console.WriteLine($"  {new string(' ', depth * 2)}{(depth == 0 ? "" : $"[{part.ParentSlot}] ")}{part}");
        foreach (var child in part.Children.OrderBy(c => c.Key)) Print(child.Value, depth + 1);
    }

    private static string Short(string id) => id.Length > 48 ? id[^48..] : id;
}
