using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.AC;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Services.Catalog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Street_Rod_AC.Services.Race;

/// <summary>
/// Service for creating and launching drag races
/// </summary>
public class DragRaceService
{
    private readonly RaceConfigurationService _raceConfigService;
    private readonly IContentCatalogRepository _catalogRepository;
    private readonly IAppLogger _logger;

    public DragRaceService(IContentCatalogRepository catalogRepository)
    {
        _raceConfigService = new RaceConfigurationService();
        _catalogRepository = catalogRepository;
        _logger = AppLoggerFactory.CreateLogger("DragRaceService");
    }

    /// <summary>
    /// Create a drag race configuration with player vs opponent
    /// </summary>
    public RaceConfiguration CreateDragRaceConfiguration(
        Car playerCar,
        string playerName,
        UsedCarListing opponentCar,
        string opponentName,
        string acInstallPath)
    {
        _logger.Information("Creating drag race: {PlayerName} vs {OpponentName}", playerName, opponentName);

        // Get car definitions from catalog
        var playerCarDef = _catalogRepository.GetCar(playerCar.DefinitionId);
        var opponentCarDef = _catalogRepository.GetCar(opponentCar.CarDefinitionId);

        if (playerCarDef == null)
        {
            throw new InvalidOperationException($"Player car definition not found: {playerCar.DefinitionId}");
        }

        if (opponentCarDef == null)
        {
            throw new InvalidOperationException($"Opponent car definition not found: {opponentCar.CarDefinitionId}");
        }

        var config = new RaceConfiguration();

        // Standard sections (based on drag_race.ini template)
        config.Benchmark.Active = 0;

        config.DynamicTrack.LapGain = 1;
        config.DynamicTrack.Randomness = 0;
        config.DynamicTrack.SessionStart = 100;
        config.DynamicTrack.SessionTransfer = 100;
        config.DynamicTrack.Preset = 5;

        config.GhostCar.Enabled = 0;
        config.GhostCar.File = string.Empty;
        config.GhostCar.Load = 0;
        config.GhostCar.Playing = 0;
        config.GhostCar.Recording = 0;
        config.GhostCar.SecondsAdvantage = 0;

        config.Groove.VirtualLaps = 10;
        config.Groove.MaxLaps = 30;
        config.Groove.StartingLaps = 0;

        config.Header.Version = 1;
        config.Header.CmFeatureSet = 2;

        config.LapInvalidator.AllowedTyresOut = -1;

        config.Lighting.CloudSpeed = 0.200;
        config.Lighting.SunAngle = -16.00;
        config.Lighting.TimeMult = 1.0;
        config.Lighting.CmWeatherType = 19;
        config.Lighting.TrackGeotagLong = 0.231944444444444;
        config.Lighting.TrackTimezoneBaseOffset = 3600;
        config.Lighting.TrackTimezoneDts = 0;
        config.Lighting.TrackTimezoneOffset = 3600;
        config.Lighting.TrackGeotagLat = 47.9602777777778;
        config.Lighting.CmWeatherController = "base";

        config.Options.UseMph = 0;

        // RACE section - Player car model and settings
        config.Race.AiLevel = 100;
        config.Race.Cars = 2;  // Player + 1 opponent
        config.Race.ConfigTrack = "drag1000";  // 1/4 mile drag strip configuration
        config.Race.DriftMode = 0;
        config.Race.FixedSetup = 0;
        config.Race.JumpStartPenalty = 1;
        config.Race.Model = playerCarDef.Id;  // Player's car model ID
        config.Race.ModelConfig = string.Empty;
        config.Race.Penalties = 0;
        config.Race.RaceLaps = 1;  // One lap for drag race
        config.Race.Skin = playerCar.SkinId;  // Player's skin
        config.Race.Track = "ks_drag";  // Kunos drag strip

        config.Remote.Active = 0;
        config.Remote.Guid = string.Empty;
        config.Remote.Name = string.Empty;
        config.Remote.Password = string.Empty;
        config.Remote.RequestedCar = string.Empty;
        config.Remote.ServerIp = string.Empty;
        config.Remote.ServerPort = string.Empty;
        config.Remote.Team = string.Empty;

        config.Replay.Active = 0;
        config.Replay.Filename = string.Empty;

        config.Restart.Active = 0;

        config.Temperature.Ambient = 12;
        config.Temperature.Road = 13;

        config.Weather.Name = "4_mid_clear";

        config.Wind.DirectionDeg = -1;
        config.Wind.SpeedKmhMax = 40;
        config.Wind.SpeedKmhMin = 2;

        config.PreviewGeneration.Active = 0;

        // CAR_0 - Player car (MODEL="-" means inherit from RACE section)
        config.Cars.Add(new RaceCar
        {
            Index = 0,
            Model = "-",  // Special value indicating player car
            Skin = playerCar.SkinId,
            Setup = string.Empty,
            ModelConfig = string.Empty,
            AiLevel = 0,  // Not AI
            AiAggression = 0,  // Not AI
            DriverName = playerName,
            Ballast = 0,
            Restrictor = 0,
            NationCode = "USA",  // Default
            Nationality = "USA"
        });

        // CAR_1 - Opponent car (all properties set here)
        config.Cars.Add(new RaceCar
        {
            Index = 1,
            Model = opponentCarDef.Id,
            Skin = opponentCar.SkinId,
            Setup = string.Empty,
            ModelConfig = string.Empty,
            AiLevel = 101,  // High AI level for competitive race
            AiAggression = 100,
            DriverName = opponentName,
            Ballast = 0,
            Restrictor = 0,
            NationCode = "USA",
            Nationality = "USA"
        });

        // SESSION_0 - Drag race session
        config.Sessions.Add(new RaceSession
        {
            Index = 0,
            Name = "Drag Race",
            Type = 7,  // Type 7 is drag race
            DurationMinutes = 0,  // No time limit
            SpawnSet = "START"
        });

        return config;
    }

    /// <summary>
    /// Save race configuration to Documents\Assetto Corsa\cfg\race.ini using template
    /// </summary>
    public void SaveRaceConfigurationFromTemplate(
        string playerCarId,
        string playerSkin,
        string opponentCarId,
        string opponentSkin,
        string playerName,
        string opponentName)
    {
        // Get paths
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var acDocumentsPath = Path.Combine(documentsPath, "Assetto Corsa");
        var cfgPath = Path.Combine(acDocumentsPath, "cfg");
        var raceIniPath = Path.Combine(cfgPath, "race.ini");

        // Template path (in project)
        var templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Project Guidelines", "examples", "drag_race.ini");

        _logger.Information("Saving drag race configuration to {Path}", raceIniPath);

        // Ensure cfg directory exists
        if (!Directory.Exists(cfgPath))
        {
            Directory.CreateDirectory(cfgPath);
            _logger.Information("Created cfg directory at {Path}", cfgPath);
        }

        // Backup existing race.ini if it exists
        if (File.Exists(raceIniPath))
        {
            var backupPath = Path.Combine(cfgPath, $"race.ini.backup.{DateTime.Now:yyyyMMddHHmmss}");
            File.Copy(raceIniPath, backupPath, true);
            _logger.Information("Backed up existing race.ini to {BackupPath}", backupPath);
        }

        // Read template
        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException($"Drag race template not found at: {templatePath}");
        }

        var templateContent = File.ReadAllText(templatePath);

        // Replace values in template
        // RACE section: MODEL and SKIN (for player car)
        templateContent = ReplaceLine(templateContent, "[RACE]", "MODEL", playerCarId);
        templateContent = ReplaceLine(templateContent, "[RACE]", "SKIN", playerSkin);

        // CAR_0 section (player)
        templateContent = ReplaceLine(templateContent, "[CAR_0]", "SKIN", playerSkin);
        templateContent = ReplaceLine(templateContent, "[CAR_0]", "DRIVER_NAME", playerName);

        // CAR_1 section (opponent)
        templateContent = ReplaceLine(templateContent, "[CAR_1]", "MODEL", opponentCarId);
        templateContent = ReplaceLine(templateContent, "[CAR_1]", "SKIN", opponentSkin);
        templateContent = ReplaceLine(templateContent, "[CAR_1]", "DRIVER_NAME", opponentName);

        // Write to race.ini
        File.WriteAllText(raceIniPath, templateContent);
        _logger.Information("Race configuration saved successfully");
    }

    /// <summary>
    /// Replace a specific key value in an INI section
    /// </summary>
    private string ReplaceLine(string content, string section, string key, string newValue)
    {
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var inSection = false;
        var result = new List<string>();

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // Check if we're entering the target section
            if (trimmedLine.Equals(section, StringComparison.OrdinalIgnoreCase))
            {
                inSection = true;
                result.Add(line);
                continue;
            }

            // Check if we're leaving the section
            if (inSection && trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
            {
                inSection = false;
            }

            // If we're in the target section and this line has the key
            if (inSection && trimmedLine.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
            {
                // Replace the value
                result.Add($"{key}={newValue}");
            }
            else
            {
                result.Add(line);
            }
        }

        return string.Join(Environment.NewLine, result);
    }

    /// <summary>
    /// Select a random opponent car from the used car market
    /// </summary>
    public UsedCarListing? SelectRandomOpponent(GameState gameState)
    {
        var availableCars = gameState.UsedCarMarket
            .Where(c => !c.IsSold)
            .ToList();

        if (availableCars.Count == 0)
        {
            _logger.Warning("No available cars in used car market for opponent");
            return null;
        }

        var random = new Random();
        var index = random.Next(availableCars.Count);
        var selectedCar = availableCars[index];

        _logger.Information("Selected random opponent: {CarId} (Skin: {Skin})",
            selectedCar.CarDefinitionId, selectedCar.SkinId);

        return selectedCar;
    }

    /// <summary>
    /// Generate a random opponent name
    /// </summary>
    public string GenerateOpponentName()
    {
        var firstNames = new[]
        {
            "Billy", "Chuck", "Carlo", "Matt", "Dave", "Pavel", "Shura", "Danny",
            "Rick", "Percy", "Klej", "Mal", "Shawn", "Dex", "Kevin", "Andrea",
            "Sakon", "Klingon", "Willy", "Michael", "Holly", "Krosty", "Mario",
            "Kito", "Dick", "Pete", "Andy", "Hugo", "Hans", "Dieter", "Mark",
            "Pierre", "Hassan", "Zeke", "Trump", "Elber", "Cop", "Bolo"
        };

        var lastNames = new[]
        {
            "Lampazzo", "Tornado", "Pappolongo", "LaPallita", "Gargiulo", "Voludin",
            "McPinch", "Paletto", "Pegasus", "Goodpace", "Bergan", "Armando",
            "Cachamai", "Winrace", "Pirongo", "Crotto", "Matraka", "Popinsky",
            "Sandia", "Birulo", "Sikorsky", "Ganzua", "Ravioli", "Banzai",
            "Palumbo", "Slipstream", "Mendieta", "Fast", "Footdown", "Von Heil",
            "Fullthrottle", "Du Mans", "Sali Baba", "Garloch", "Miranda", "Gamota",
            "Mulligan", "Fekal"
        };

        var random = new Random();
        var firstName = firstNames[random.Next(firstNames.Length)];
        var lastName = lastNames[random.Next(lastNames.Length)];

        return $"{firstName} {lastName}";
    }
}
