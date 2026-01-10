using System.Collections.Generic;

namespace Street_Rod_AC.Models.AC;

/// <summary>
/// Complete race configuration for Assetto Corsa (race.ini)
/// </summary>
public class RaceConfiguration
{
    public BenchmarkSection Benchmark { get; set; } = new();
    public DynamicTrackSection DynamicTrack { get; set; } = new();
    public GhostCarSection GhostCar { get; set; } = new();
    public GrooveSection Groove { get; set; } = new();
    public HeaderSection Header { get; set; } = new();
    public LapInvalidatorSection LapInvalidator { get; set; } = new();
    public LightingSection Lighting { get; set; } = new();
    public OptionsSection Options { get; set; } = new();
    public RaceSection Race { get; set; } = new();
    public RemoteSection Remote { get; set; } = new();
    public ReplaySection Replay { get; set; } = new();
    public RestartSection Restart { get; set; } = new();
    public TemperatureSection Temperature { get; set; } = new();
    public WeatherSection Weather { get; set; } = new();
    public WindSection Wind { get; set; } = new();
    public PreviewGenerationSection PreviewGeneration { get; set; } = new();

    /// <summary>
    /// List of cars participating in the race (CAR_0 through CAR_39)
    /// CAR_0 is typically the player
    /// </summary>
    public List<RaceCar> Cars { get; set; } = new();

    /// <summary>
    /// List of race sessions (SESSION_0, SESSION_1, etc.)
    /// </summary>
    public List<RaceSession> Sessions { get; set; } = new();
}

public class BenchmarkSection
{
    public int Active { get; set; }
}

public class DynamicTrackSection
{
    public int LapGain { get; set; }
    public int Randomness { get; set; }
    public int SessionStart { get; set; }
    public int SessionTransfer { get; set; }
    public int Preset { get; set; }
}

public class GhostCarSection
{
    public int Enabled { get; set; }
    public string File { get; set; } = string.Empty;
    public int Load { get; set; }
    public int Playing { get; set; }
    public int Recording { get; set; }
    public int SecondsAdvantage { get; set; }
}

public class GrooveSection
{
    public int VirtualLaps { get; set; }
    public int MaxLaps { get; set; }
    public int StartingLaps { get; set; }
}

public class HeaderSection
{
    public int Version { get; set; }
    public int CmFeatureSet { get; set; }
}

public class LapInvalidatorSection
{
    public int AllowedTyresOut { get; set; }
}

public class LightingSection
{
    public double CloudSpeed { get; set; }
    public double SunAngle { get; set; }
    public double TimeMult { get; set; }
    public int CmWeatherType { get; set; }
    public double TrackGeotagLong { get; set; }
    public int TrackTimezoneBaseOffset { get; set; }
    public int TrackTimezoneDts { get; set; }
    public int TrackTimezoneOffset { get; set; }
    public double TrackGeotagLat { get; set; }
    public string CmWeatherController { get; set; } = string.Empty;
}

public class OptionsSection
{
    public int UseMph { get; set; }
}

public class RaceSection
{
    public int AiLevel { get; set; }
    public int Cars { get; set; }
    public string ConfigTrack { get; set; } = string.Empty;
    public int DriftMode { get; set; }
    public int FixedSetup { get; set; }
    public int JumpStartPenalty { get; set; }
    public string Model { get; set; } = string.Empty;
    public string ModelConfig { get; set; } = string.Empty;
    public int Penalties { get; set; }
    public int RaceLaps { get; set; }
    public string Skin { get; set; } = string.Empty;
    public string Track { get; set; } = string.Empty;
}

public class RemoteSection
{
    public int Active { get; set; }
    public string Guid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string RequestedCar { get; set; } = string.Empty;
    public string ServerIp { get; set; } = string.Empty;
    public string ServerPort { get; set; } = string.Empty;
    public string Team { get; set; } = string.Empty;
}

public class ReplaySection
{
    public int Active { get; set; }
    public string Filename { get; set; } = string.Empty;
}

public class RestartSection
{
    public int Active { get; set; }
}

public class TemperatureSection
{
    public int Ambient { get; set; }
    public int Road { get; set; }
}

public class WeatherSection
{
    public string Name { get; set; } = string.Empty;
}

public class WindSection
{
    public int DirectionDeg { get; set; }
    public int SpeedKmhMax { get; set; }
    public int SpeedKmhMin { get; set; }
}

public class PreviewGenerationSection
{
    public int Active { get; set; }
}

/// <summary>
/// Represents a single car entry in the race (CAR_0, CAR_1, etc.)
/// CAR_0 is typically the player's car (indicated by MODEL="-")
/// </summary>
public class RaceCar
{
    /// <summary>
    /// Car index (0-39 for a 40-car race)
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Car model ID ("-" for player car inherits from RACE section)
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Skin name
    /// </summary>
    public string Skin { get; set; } = string.Empty;

    /// <summary>
    /// Setup file name (empty for default)
    /// </summary>
    public string Setup { get; set; } = string.Empty;

    /// <summary>
    /// Model configuration
    /// </summary>
    public string ModelConfig { get; set; } = string.Empty;

    /// <summary>
    /// AI difficulty level (0-100)
    /// </summary>
    public double AiLevel { get; set; }

    /// <summary>
    /// AI aggression level (0-100)
    /// </summary>
    public int AiAggression { get; set; }

    /// <summary>
    /// Driver name
    /// </summary>
    public string DriverName { get; set; } = string.Empty;

    /// <summary>
    /// Ballast weight (kg)
    /// </summary>
    public int Ballast { get; set; }

    /// <summary>
    /// Air restrictor percentage
    /// </summary>
    public int Restrictor { get; set; }

    /// <summary>
    /// Nation code (e.g., "USA", "ITA", "JPN")
    /// </summary>
    public string NationCode { get; set; } = string.Empty;

    /// <summary>
    /// Full nationality name
    /// </summary>
    public string Nationality { get; set; } = string.Empty;

    /// <summary>
    /// Optional: Driven distance (Content Manager feature)
    /// </summary>
    public double? CmDrivenDistance { get; set; }

    /// <summary>
    /// True if this is the player's car (CAR_0 with MODEL="-")
    /// </summary>
    public bool IsPlayer => Index == 0;
}

/// <summary>
/// Represents a race session configuration (SESSION_0, SESSION_1, etc.)
/// </summary>
public class RaceSession
{
    /// <summary>
    /// Session index (0, 1, 2, etc.)
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Session name (e.g., "Practice", "Qualifying", "Race")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Session type:
    /// 1 = Practice
    /// 2 = Qualifying
    /// 3 = Race
    /// </summary>
    public int Type { get; set; }

    /// <summary>
    /// Session duration in minutes (0 for unlimited/lap-based)
    /// </summary>
    public int DurationMinutes { get; set; }

    /// <summary>
    /// Spawn location (e.g., "PIT", "START")
    /// </summary>
    public string SpawnSet { get; set; } = string.Empty;
}
