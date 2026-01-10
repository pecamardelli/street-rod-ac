using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.AC;
using Street_Rod_AC.Services.Configuration.Models;
using Street_Rod_AC.Services.Configuration.Parsers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Street_Rod_AC.Services.Race;

/// <summary>
/// Service for reading and writing Assetto Corsa race.ini files
/// </summary>
public class RaceConfigurationService
{
    private readonly IniParser _parser;
    private readonly IniWriter _writer;
    private readonly IAppLogger _logger;

    public RaceConfigurationService()
    {
        _parser = new IniParser();
        _writer = new IniWriter();
        _logger = AppLoggerFactory.CreateLogger("RaceConfigurationService");
    }

    /// <summary>
    /// Load a race configuration from a race.ini file
    /// </summary>
    public RaceConfiguration LoadFromFile(string filePath)
    {
        _logger.Information("Loading race configuration from {FilePath}", filePath);
        var iniFile = _parser.Parse(filePath);
        return ParseRaceConfiguration(iniFile);
    }

    /// <summary>
    /// Save a race configuration to a race.ini file
    /// </summary>
    public void SaveToFile(RaceConfiguration config, string filePath)
    {
        _logger.Information("Saving race configuration to {FilePath}", filePath);
        var iniFile = BuildIniFile(config, filePath);
        _writer.Write(iniFile);
    }

    /// <summary>
    /// Parse an IniFile into a RaceConfiguration model
    /// </summary>
    private RaceConfiguration ParseRaceConfiguration(IniFile iniFile)
    {
        var config = new RaceConfiguration();

        // Parse all standard sections
        config.Benchmark = ParseBenchmarkSection(iniFile);
        config.DynamicTrack = ParseDynamicTrackSection(iniFile);
        config.GhostCar = ParseGhostCarSection(iniFile);
        config.Groove = ParseGrooveSection(iniFile);
        config.Header = ParseHeaderSection(iniFile);
        config.LapInvalidator = ParseLapInvalidatorSection(iniFile);
        config.Lighting = ParseLightingSection(iniFile);
        config.Options = ParseOptionsSection(iniFile);
        config.Race = ParseRaceSection(iniFile);
        config.Remote = ParseRemoteSection(iniFile);
        config.Replay = ParseReplaySection(iniFile);
        config.Restart = ParseRestartSection(iniFile);
        config.Temperature = ParseTemperatureSection(iniFile);
        config.Weather = ParseWeatherSection(iniFile);
        config.Wind = ParseWindSection(iniFile);
        config.PreviewGeneration = ParsePreviewGenerationSection(iniFile);

        // Parse car sections (CAR_0, CAR_1, ..., CAR_39)
        config.Cars = ParseCarSections(iniFile);

        // Parse session sections (SESSION_0, SESSION_1, ...)
        config.Sessions = ParseSessionSections(iniFile);

        _logger.Information("Loaded race configuration with {CarCount} cars and {SessionCount} sessions",
            config.Cars.Count, config.Sessions.Count);

        return config;
    }

    private BenchmarkSection ParseBenchmarkSection(IniFile iniFile)
    {
        return new BenchmarkSection
        {
            Active = GetIntValue(iniFile, "BENCHMARK", "ACTIVE")
        };
    }

    private DynamicTrackSection ParseDynamicTrackSection(IniFile iniFile)
    {
        return new DynamicTrackSection
        {
            LapGain = GetIntValue(iniFile, "DYNAMIC_TRACK", "LAP_GAIN"),
            Randomness = GetIntValue(iniFile, "DYNAMIC_TRACK", "RANDOMNESS"),
            SessionStart = GetIntValue(iniFile, "DYNAMIC_TRACK", "SESSION_START"),
            SessionTransfer = GetIntValue(iniFile, "DYNAMIC_TRACK", "SESSION_TRANSFER"),
            Preset = GetIntValue(iniFile, "DYNAMIC_TRACK", "PRESET")
        };
    }

    private GhostCarSection ParseGhostCarSection(IniFile iniFile)
    {
        return new GhostCarSection
        {
            Enabled = GetIntValue(iniFile, "GHOST_CAR", "ENABLED"),
            File = GetStringValue(iniFile, "GHOST_CAR", "FILE"),
            Load = GetIntValue(iniFile, "GHOST_CAR", "LOAD"),
            Playing = GetIntValue(iniFile, "GHOST_CAR", "PLAYING"),
            Recording = GetIntValue(iniFile, "GHOST_CAR", "RECORDING"),
            SecondsAdvantage = GetIntValue(iniFile, "GHOST_CAR", "SECONDS_ADVANTAGE")
        };
    }

    private GrooveSection ParseGrooveSection(IniFile iniFile)
    {
        return new GrooveSection
        {
            VirtualLaps = GetIntValue(iniFile, "GROOVE", "VIRTUAL_LAPS"),
            MaxLaps = GetIntValue(iniFile, "GROOVE", "MAX_LAPS"),
            StartingLaps = GetIntValue(iniFile, "GROOVE", "STARTING_LAPS")
        };
    }

    private HeaderSection ParseHeaderSection(IniFile iniFile)
    {
        return new HeaderSection
        {
            Version = GetIntValue(iniFile, "HEADER", "VERSION"),
            CmFeatureSet = GetIntValue(iniFile, "HEADER", "__CM_FEATURE_SET")
        };
    }

    private LapInvalidatorSection ParseLapInvalidatorSection(IniFile iniFile)
    {
        return new LapInvalidatorSection
        {
            AllowedTyresOut = GetIntValue(iniFile, "LAP_INVALIDATOR", "ALLOWED_TYRES_OUT")
        };
    }

    private LightingSection ParseLightingSection(IniFile iniFile)
    {
        return new LightingSection
        {
            CloudSpeed = GetDoubleValue(iniFile, "LIGHTING", "CLOUD_SPEED"),
            SunAngle = GetDoubleValue(iniFile, "LIGHTING", "SUN_ANGLE"),
            TimeMult = GetDoubleValue(iniFile, "LIGHTING", "TIME_MULT"),
            CmWeatherType = GetIntValue(iniFile, "LIGHTING", "__CM_WEATHER_TYPE"),
            TrackGeotagLong = GetDoubleValue(iniFile, "LIGHTING", "__TRACK_GEOTAG_LONG"),
            TrackTimezoneBaseOffset = GetIntValue(iniFile, "LIGHTING", "__TRACK_TIMEZONE_BASE_OFFSET"),
            TrackTimezoneDts = GetIntValue(iniFile, "LIGHTING", "__TRACK_TIMEZONE_DTS"),
            TrackTimezoneOffset = GetIntValue(iniFile, "LIGHTING", "__TRACK_TIMEZONE_OFFSET"),
            TrackGeotagLat = GetDoubleValue(iniFile, "LIGHTING", "__TRACK_GEOTAG_LAT"),
            CmWeatherController = GetStringValue(iniFile, "LIGHTING", "__CM_WEATHER_CONTROLLER")
        };
    }

    private OptionsSection ParseOptionsSection(IniFile iniFile)
    {
        return new OptionsSection
        {
            UseMph = GetIntValue(iniFile, "OPTIONS", "USE_MPH")
        };
    }

    private RaceSection ParseRaceSection(IniFile iniFile)
    {
        return new RaceSection
        {
            AiLevel = GetIntValue(iniFile, "RACE", "AI_LEVEL"),
            Cars = GetIntValue(iniFile, "RACE", "CARS"),
            ConfigTrack = GetStringValue(iniFile, "RACE", "CONFIG_TRACK"),
            DriftMode = GetIntValue(iniFile, "RACE", "DRIFT_MODE"),
            FixedSetup = GetIntValue(iniFile, "RACE", "FIXED_SETUP"),
            JumpStartPenalty = GetIntValue(iniFile, "RACE", "JUMP_START_PENALTY"),
            Model = GetStringValue(iniFile, "RACE", "MODEL"),
            ModelConfig = GetStringValue(iniFile, "RACE", "MODEL_CONFIG"),
            Penalties = GetIntValue(iniFile, "RACE", "PENALTIES"),
            RaceLaps = GetIntValue(iniFile, "RACE", "RACE_LAPS"),
            Skin = GetStringValue(iniFile, "RACE", "SKIN"),
            Track = GetStringValue(iniFile, "RACE", "TRACK")
        };
    }

    private RemoteSection ParseRemoteSection(IniFile iniFile)
    {
        return new RemoteSection
        {
            Active = GetIntValue(iniFile, "REMOTE", "ACTIVE"),
            Guid = GetStringValue(iniFile, "REMOTE", "GUID"),
            Name = GetStringValue(iniFile, "REMOTE", "NAME"),
            Password = GetStringValue(iniFile, "REMOTE", "PASSWORD"),
            RequestedCar = GetStringValue(iniFile, "REMOTE", "REQUESTED_CAR"),
            ServerIp = GetStringValue(iniFile, "REMOTE", "SERVER_IP"),
            ServerPort = GetStringValue(iniFile, "REMOTE", "SERVER_PORT"),
            Team = GetStringValue(iniFile, "REMOTE", "TEAM")
        };
    }

    private ReplaySection ParseReplaySection(IniFile iniFile)
    {
        return new ReplaySection
        {
            Active = GetIntValue(iniFile, "REPLAY", "ACTIVE"),
            Filename = GetStringValue(iniFile, "REPLAY", "FILENAME")
        };
    }

    private RestartSection ParseRestartSection(IniFile iniFile)
    {
        return new RestartSection
        {
            Active = GetIntValue(iniFile, "RESTART", "ACTIVE")
        };
    }

    private TemperatureSection ParseTemperatureSection(IniFile iniFile)
    {
        return new TemperatureSection
        {
            Ambient = GetIntValue(iniFile, "TEMPERATURE", "AMBIENT"),
            Road = GetIntValue(iniFile, "TEMPERATURE", "ROAD")
        };
    }

    private WeatherSection ParseWeatherSection(IniFile iniFile)
    {
        return new WeatherSection
        {
            Name = GetStringValue(iniFile, "WEATHER", "NAME")
        };
    }

    private WindSection ParseWindSection(IniFile iniFile)
    {
        return new WindSection
        {
            DirectionDeg = GetIntValue(iniFile, "WIND", "DIRECTION_DEG"),
            SpeedKmhMax = GetIntValue(iniFile, "WIND", "SPEED_KMH_MAX"),
            SpeedKmhMin = GetIntValue(iniFile, "WIND", "SPEED_KMH_MIN")
        };
    }

    private PreviewGenerationSection ParsePreviewGenerationSection(IniFile iniFile)
    {
        return new PreviewGenerationSection
        {
            Active = GetIntValue(iniFile, "__PREVIEW_GENERATION", "ACTIVE")
        };
    }

    /// <summary>
    /// Parse all CAR_XX sections
    /// </summary>
    private List<RaceCar> ParseCarSections(IniFile iniFile)
    {
        var cars = new List<RaceCar>();

        // Find all CAR_XX sections
        var carSections = iniFile.Sections
            .Where(s => s.Name.StartsWith("CAR_", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var section in carSections)
        {
            // Extract car index from section name (CAR_0 -> 0, CAR_39 -> 39)
            var indexStr = section.Name.Substring(4);
            if (int.TryParse(indexStr, out int index))
            {
                var car = new RaceCar
                {
                    Index = index,
                    Model = section.GetKey("MODEL")?.Value ?? string.Empty,
                    Skin = section.GetKey("SKIN")?.Value ?? string.Empty,
                    Setup = section.GetKey("SETUP")?.Value ?? string.Empty,
                    ModelConfig = section.GetKey("MODEL_CONFIG")?.Value ?? string.Empty,
                    AiLevel = GetDoubleValue(section, "AI_LEVEL"),
                    AiAggression = GetIntValue(section, "AI_AGGRESSION"),
                    DriverName = section.GetKey("DRIVER_NAME")?.Value ?? string.Empty,
                    Ballast = GetIntValue(section, "BALLAST"),
                    Restrictor = GetIntValue(section, "RESTRICTOR"),
                    NationCode = section.GetKey("NATION_CODE")?.Value ?? string.Empty,
                    Nationality = section.GetKey("NATIONALITY")?.Value ?? string.Empty
                };

                // Optional CM-specific field
                var drivenDistanceValue = section.GetKey("__CM_DRIVEN_DISTANCE")?.Value;
                if (!string.IsNullOrEmpty(drivenDistanceValue) &&
                    double.TryParse(drivenDistanceValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double drivenDistance))
                {
                    car.CmDrivenDistance = drivenDistance;
                }

                cars.Add(car);
            }
        }

        // Sort by index
        return cars.OrderBy(c => c.Index).ToList();
    }

    /// <summary>
    /// Parse all SESSION_X sections
    /// </summary>
    private List<RaceSession> ParseSessionSections(IniFile iniFile)
    {
        var sessions = new List<RaceSession>();

        // Find all SESSION_X sections
        var sessionSections = iniFile.Sections
            .Where(s => s.Name.StartsWith("SESSION_", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var section in sessionSections)
        {
            // Extract session index from section name (SESSION_0 -> 0)
            var indexStr = section.Name.Substring(8);
            if (int.TryParse(indexStr, out int index))
            {
                var session = new RaceSession
                {
                    Index = index,
                    Name = section.GetKey("NAME")?.Value ?? string.Empty,
                    Type = GetIntValue(section, "TYPE"),
                    DurationMinutes = GetIntValue(section, "DURATION_MINUTES"),
                    SpawnSet = section.GetKey("SPAWN_SET")?.Value ?? string.Empty
                };

                sessions.Add(session);
            }
        }

        // Sort by index
        return sessions.OrderBy(s => s.Index).ToList();
    }

    /// <summary>
    /// Build an IniFile from a RaceConfiguration model
    /// </summary>
    private IniFile BuildIniFile(RaceConfiguration config, string filePath)
    {
        var iniFile = new IniFile(filePath);

        // Build all sections
        BuildBenchmarkSection(iniFile, config.Benchmark);
        BuildDynamicTrackSection(iniFile, config.DynamicTrack);
        BuildGhostCarSection(iniFile, config.GhostCar);
        BuildGrooveSection(iniFile, config.Groove);
        BuildHeaderSection(iniFile, config.Header);
        BuildLapInvalidatorSection(iniFile, config.LapInvalidator);
        BuildLightingSection(iniFile, config.Lighting);
        BuildOptionsSection(iniFile, config.Options);
        BuildRaceSection(iniFile, config.Race);
        BuildRemoteSection(iniFile, config.Remote);
        BuildReplaySection(iniFile, config.Replay);
        BuildRestartSection(iniFile, config.Restart);
        BuildTemperatureSection(iniFile, config.Temperature);
        BuildWeatherSection(iniFile, config.Weather);
        BuildWindSection(iniFile, config.Wind);
        BuildPreviewGenerationSection(iniFile, config.PreviewGeneration);

        // Build car sections
        foreach (var car in config.Cars.OrderBy(c => c.Index))
        {
            BuildCarSection(iniFile, car);
        }

        // Build session sections
        foreach (var session in config.Sessions.OrderBy(s => s.Index))
        {
            BuildSessionSection(iniFile, session);
        }

        return iniFile;
    }

    private void BuildBenchmarkSection(IniFile iniFile, BenchmarkSection section)
    {
        iniFile.SetValue("BENCHMARK", "ACTIVE", section.Active.ToString());
    }

    private void BuildDynamicTrackSection(IniFile iniFile, DynamicTrackSection section)
    {
        iniFile.SetValue("DYNAMIC_TRACK", "LAP_GAIN", section.LapGain.ToString());
        iniFile.SetValue("DYNAMIC_TRACK", "RANDOMNESS", section.Randomness.ToString());
        iniFile.SetValue("DYNAMIC_TRACK", "SESSION_START", section.SessionStart.ToString());
        iniFile.SetValue("DYNAMIC_TRACK", "SESSION_TRANSFER", section.SessionTransfer.ToString());
        iniFile.SetValue("DYNAMIC_TRACK", "PRESET", section.Preset.ToString());
    }

    private void BuildGhostCarSection(IniFile iniFile, GhostCarSection section)
    {
        iniFile.SetValue("GHOST_CAR", "ENABLED", section.Enabled.ToString());
        iniFile.SetValue("GHOST_CAR", "FILE", section.File);
        iniFile.SetValue("GHOST_CAR", "LOAD", section.Load.ToString());
        iniFile.SetValue("GHOST_CAR", "PLAYING", section.Playing.ToString());
        iniFile.SetValue("GHOST_CAR", "RECORDING", section.Recording.ToString());
        iniFile.SetValue("GHOST_CAR", "SECONDS_ADVANTAGE", section.SecondsAdvantage.ToString());
    }

    private void BuildGrooveSection(IniFile iniFile, GrooveSection section)
    {
        iniFile.SetValue("GROOVE", "VIRTUAL_LAPS", section.VirtualLaps.ToString());
        iniFile.SetValue("GROOVE", "MAX_LAPS", section.MaxLaps.ToString());
        iniFile.SetValue("GROOVE", "STARTING_LAPS", section.StartingLaps.ToString());
    }

    private void BuildHeaderSection(IniFile iniFile, HeaderSection section)
    {
        iniFile.SetValue("HEADER", "VERSION", section.Version.ToString());
        iniFile.SetValue("HEADER", "__CM_FEATURE_SET", section.CmFeatureSet.ToString());
    }

    private void BuildLapInvalidatorSection(IniFile iniFile, LapInvalidatorSection section)
    {
        iniFile.SetValue("LAP_INVALIDATOR", "ALLOWED_TYRES_OUT", section.AllowedTyresOut.ToString());
    }

    private void BuildLightingSection(IniFile iniFile, LightingSection section)
    {
        iniFile.SetValue("LIGHTING", "CLOUD_SPEED", section.CloudSpeed.ToString("0.000", CultureInfo.InvariantCulture));
        iniFile.SetValue("LIGHTING", "SUN_ANGLE", section.SunAngle.ToString("0.00", CultureInfo.InvariantCulture));
        iniFile.SetValue("LIGHTING", "TIME_MULT", section.TimeMult.ToString("0.0", CultureInfo.InvariantCulture));
        iniFile.SetValue("LIGHTING", "__CM_WEATHER_TYPE", section.CmWeatherType.ToString());
        iniFile.SetValue("LIGHTING", "__TRACK_GEOTAG_LONG", section.TrackGeotagLong.ToString(CultureInfo.InvariantCulture));
        iniFile.SetValue("LIGHTING", "__TRACK_TIMEZONE_BASE_OFFSET", section.TrackTimezoneBaseOffset.ToString());
        iniFile.SetValue("LIGHTING", "__TRACK_TIMEZONE_DTS", section.TrackTimezoneDts.ToString());
        iniFile.SetValue("LIGHTING", "__TRACK_TIMEZONE_OFFSET", section.TrackTimezoneOffset.ToString());
        iniFile.SetValue("LIGHTING", "__TRACK_GEOTAG_LAT", section.TrackGeotagLat.ToString(CultureInfo.InvariantCulture));
        iniFile.SetValue("LIGHTING", "__CM_WEATHER_CONTROLLER", section.CmWeatherController);
    }

    private void BuildOptionsSection(IniFile iniFile, OptionsSection section)
    {
        iniFile.SetValue("OPTIONS", "USE_MPH", section.UseMph.ToString());
    }

    private void BuildRaceSection(IniFile iniFile, RaceSection section)
    {
        iniFile.SetValue("RACE", "AI_LEVEL", section.AiLevel.ToString());
        iniFile.SetValue("RACE", "CARS", section.Cars.ToString());
        iniFile.SetValue("RACE", "CONFIG_TRACK", section.ConfigTrack);
        iniFile.SetValue("RACE", "DRIFT_MODE", section.DriftMode.ToString());
        iniFile.SetValue("RACE", "FIXED_SETUP", section.FixedSetup.ToString());
        iniFile.SetValue("RACE", "JUMP_START_PENALTY", section.JumpStartPenalty.ToString());
        iniFile.SetValue("RACE", "MODEL", section.Model);
        iniFile.SetValue("RACE", "MODEL_CONFIG", section.ModelConfig);
        iniFile.SetValue("RACE", "PENALTIES", section.Penalties.ToString());
        iniFile.SetValue("RACE", "RACE_LAPS", section.RaceLaps.ToString());
        iniFile.SetValue("RACE", "SKIN", section.Skin);
        iniFile.SetValue("RACE", "TRACK", section.Track);
    }

    private void BuildRemoteSection(IniFile iniFile, RemoteSection section)
    {
        iniFile.SetValue("REMOTE", "ACTIVE", section.Active.ToString());
        iniFile.SetValue("REMOTE", "GUID", section.Guid);
        iniFile.SetValue("REMOTE", "NAME", section.Name);
        iniFile.SetValue("REMOTE", "PASSWORD", section.Password);
        iniFile.SetValue("REMOTE", "REQUESTED_CAR", section.RequestedCar);
        iniFile.SetValue("REMOTE", "SERVER_IP", section.ServerIp);
        iniFile.SetValue("REMOTE", "SERVER_PORT", section.ServerPort);
        iniFile.SetValue("REMOTE", "TEAM", section.Team);
    }

    private void BuildReplaySection(IniFile iniFile, ReplaySection section)
    {
        iniFile.SetValue("REPLAY", "ACTIVE", section.Active.ToString());
        iniFile.SetValue("REPLAY", "FILENAME", section.Filename);
    }

    private void BuildRestartSection(IniFile iniFile, RestartSection section)
    {
        iniFile.SetValue("RESTART", "ACTIVE", section.Active.ToString());
    }

    private void BuildTemperatureSection(IniFile iniFile, TemperatureSection section)
    {
        iniFile.SetValue("TEMPERATURE", "AMBIENT", section.Ambient.ToString());
        iniFile.SetValue("TEMPERATURE", "ROAD", section.Road.ToString());
    }

    private void BuildWeatherSection(IniFile iniFile, WeatherSection section)
    {
        iniFile.SetValue("WEATHER", "NAME", section.Name);
    }

    private void BuildWindSection(IniFile iniFile, WindSection section)
    {
        iniFile.SetValue("WIND", "DIRECTION_DEG", section.DirectionDeg.ToString());
        iniFile.SetValue("WIND", "SPEED_KMH_MAX", section.SpeedKmhMax.ToString());
        iniFile.SetValue("WIND", "SPEED_KMH_MIN", section.SpeedKmhMin.ToString());
    }

    private void BuildPreviewGenerationSection(IniFile iniFile, PreviewGenerationSection section)
    {
        iniFile.SetValue("__PREVIEW_GENERATION", "ACTIVE", section.Active.ToString());
    }

    private void BuildCarSection(IniFile iniFile, RaceCar car)
    {
        var sectionName = $"CAR_{car.Index}";

        iniFile.SetValue(sectionName, "SETUP", car.Setup);
        iniFile.SetValue(sectionName, "SKIN", car.Skin);
        iniFile.SetValue(sectionName, "MODEL", car.Model);
        iniFile.SetValue(sectionName, "MODEL_CONFIG", car.ModelConfig);
        iniFile.SetValue(sectionName, "BALLAST", car.Ballast.ToString());
        iniFile.SetValue(sectionName, "RESTRICTOR", car.Restrictor.ToString());

        if (car.CmDrivenDistance.HasValue)
        {
            iniFile.SetValue(sectionName, "__CM_DRIVEN_DISTANCE",
                car.CmDrivenDistance.Value.ToString(CultureInfo.InvariantCulture));
        }

        iniFile.SetValue(sectionName, "DRIVER_NAME", car.DriverName);
        iniFile.SetValue(sectionName, "NATIONALITY", car.Nationality);
        iniFile.SetValue(sectionName, "NATION_CODE", car.NationCode);
        iniFile.SetValue(sectionName, "AI_LEVEL", car.AiLevel.ToString(CultureInfo.InvariantCulture));

        if (car.AiAggression > 0)
        {
            iniFile.SetValue(sectionName, "AI_AGGRESSION", car.AiAggression.ToString());
        }
    }

    private void BuildSessionSection(IniFile iniFile, RaceSession session)
    {
        var sectionName = $"SESSION_{session.Index}";

        iniFile.SetValue(sectionName, "NAME", session.Name);
        iniFile.SetValue(sectionName, "TYPE", session.Type.ToString());
        iniFile.SetValue(sectionName, "DURATION_MINUTES", session.DurationMinutes.ToString());
        iniFile.SetValue(sectionName, "SPAWN_SET", session.SpawnSet);
    }

    // Helper methods for reading values
    private string GetStringValue(IniFile iniFile, string section, string key)
    {
        return iniFile.GetValue(section, key) ?? string.Empty;
    }

    private string GetStringValue(IniSection section, string key)
    {
        return section.GetKey(key)?.Value ?? string.Empty;
    }

    private int GetIntValue(IniFile iniFile, string section, string key)
    {
        var value = iniFile.GetValue(section, key);
        if (string.IsNullOrEmpty(value)) return 0;
        return int.TryParse(value, out int result) ? result : 0;
    }

    private int GetIntValue(IniSection section, string key)
    {
        var value = section.GetKey(key)?.Value;
        if (string.IsNullOrEmpty(value)) return 0;
        return int.TryParse(value, out int result) ? result : 0;
    }

    private double GetDoubleValue(IniFile iniFile, string section, string key)
    {
        var value = iniFile.GetValue(section, key);
        if (string.IsNullOrEmpty(value)) return 0.0;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) ? result : 0.0;
    }

    private double GetDoubleValue(IniSection section, string key)
    {
        var value = section.GetKey(key)?.Value;
        if (string.IsNullOrEmpty(value)) return 0.0;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) ? result : 0.0;
    }
}
