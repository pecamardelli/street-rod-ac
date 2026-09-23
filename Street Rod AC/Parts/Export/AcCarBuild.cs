using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Parts.Logic;

namespace Street_Rod_AC.Parts.Export;

/// <summary>A car's parts, as far as the export needs them</summary>
public sealed class CarBuild
{
    /// <summary>The engine on the dyno; null for a car without one</summary>
    public EngineReport? Engine { get; init; }

    /// <summary>Kilograms of the engine the car's data was made with, for the mass</summary>
    public double? FactoryEngineMass { get; init; }

    /// <summary>What is on the wheel slots; null leaves the car's running gear as its data has it</summary>
    public CornerParts[]? RunningGear { get; init; }

    /// <summary>The running gear the car's data describes</summary>
    public (RunningGearFactory.AxleParts Front, RunningGearFactory.AxleParts Rear)? FactoryRunningGear { get; init; }
}

public sealed class CarBuildResult
{
    /// <summary>File name to new content, for the files of the car's data that change</summary>
    public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The sound the engine races with; null keeps the car's own</summary>
    public CarSound? Sound { get; set; }

    /// <summary>What keeps the car from being driven, in words for the player; empty when it can go</summary>
    public List<string> Problems { get; } = new();

    public bool CanDrive => Problems.Count == 0;
}

/// <summary>
/// Everything a car's parts change in its Assetto Corsa data: the engine and transmission
/// (<see cref="AcEngineData"/>) and the running gear (<see cref="AcRunningGearData"/>), one file set.
/// </summary>
public static class AcCarBuild
{
    public static CarBuildResult Generate(PartsCatalog catalog, CarBuild build, Func<string, string?> readFile, AcEngineDataOptions? options = null)
    {
        var result = new CarBuildResult();

        if (build.Engine == null) result.Problems.Add("the car has no engine");
        else if (!build.Engine.Runs) result.Problems.Add("the engine does not run: " + (build.Engine.Problem ?? "no power"));
        else
        {
            options ??= new AcEngineDataOptions();
            options.FactoryEngineMass = build.FactoryEngineMass;
            foreach (var (name, content) in AcEngineData.Generate(build.Engine, readFile, options)) result.Files[name] = content;
        }

        if (build.RunningGear != null && build.FactoryRunningGear is { } factory)
        {
            // The running gear reads what the engine left (the mass in car.ini) before the car's own files
            string? Read(string name) => result.Files.GetValueOrDefault(name) ?? readFile(name);
            foreach (var (name, content) in AcRunningGearData.Generate(catalog, build.RunningGear, factory, Read, result.Problems)) result.Files[name] = content;
        }

        return result;
    }
}
