using System.IO;
using System.Linq;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using Street_Rod_AC.Configuration;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.AC;
using Street_Rod_AC.Parts.Export;

namespace Street_Rod_AC.Services;

/// <summary>
/// Service for discovering and loading Assetto Corsa content
/// </summary>
public class AssettoCorsaContentService : IAssettoCorsaContentService
{
    private readonly AppSettings _settings;
    private readonly List<CarInfo> _cars = [];
    private readonly List<TrackInfo> _tracks = [];
    private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger(LogCategory.Import);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public AssettoCorsaContentService()
    {
        _settings = AppSettings.Instance;
    }

    public async Task<List<CarInfo>> LoadCarsAsync()
    {
        _cars.Clear();

        if (!Directory.Exists(_settings.CarsPath))
        {
            var error = $"Cars directory not found: {_settings.CarsPath}";
            _logger.Error("Cars directory not found: {CarsPath}", _settings.CarsPath);
            throw new DirectoryNotFoundException(error);
        }

        // Every car folder with a ui\ui_car.json; the copies made for a race are not cars of the install
        var carFolders = AcCarFolder.InstalledCars(_settings.CarsPath)
            .Where(folder => File.Exists(AcCarUi.PathIn(folder)))
            .ToList();
        _logger.Information("Found {Count} ui_car.json files in {CarsPath}", carFolders.Count, _settings.CarsPath);

        var cars = await Task.Run(() =>
        {
            var loaded = new List<CarInfo>();
            foreach (var carFolder in carFolders)
            {
                try
                {
                    // Read the one lenient way every reader of ui_car.json reads it; the folder name is the car ID
                    var ui = AcCarUi.TryRead(carFolder, out var error);
                    if (ui == null)
                    {
                        _logger.Warning("Car {Folder} skipped: {Error}", carFolder, error);
                        continue;
                    }

                    var carInfo = ToCarInfo(ui);
                    carInfo.CarId = Path.GetFileName(carFolder);
                    carInfo.FolderPath = carFolder;
                    loaded.Add(carInfo);
                    _logger.Debug("Loaded car: {Name} ({CarId}) from {Folder}", carInfo.Name, carInfo.CarId, carFolder);
                }
                catch (Exception ex)
                {
                    // Log and continue - don't fail entire load for one bad car
                    _logger.Warning(ex, "Error loading car from {Folder}", carFolder);
                }
            }

            return loaded;
        });

        _cars.AddRange(cars);
        return _cars;
    }

    /// <summary>The fields of a ui_car.json the game shows, each read as leniently as mods write them</summary>
    private static CarInfo ToCarInfo(JObject ui)
    {
        var specs = AcCarUi.GetObject(ui, "specs");
        return new CarInfo
        {
            Name = AcCarUi.GetString(ui, "name") ?? string.Empty,
            Brand = AcCarUi.GetString(ui, "brand") ?? string.Empty,
            Description = AcCarUi.GetString(ui, "description") ?? string.Empty,
            Tags = AcCarUi.GetStrings(ui, "tags").ToList(),
            Class = AcCarUi.GetString(ui, "class") ?? string.Empty,
            Specs = specs == null ? null : new CarSpecs
            {
                Bhp = AcCarUi.GetString(specs, "bhp"),
                Torque = AcCarUi.GetString(specs, "torque"),
                Weight = AcCarUi.GetString(specs, "weight"),
                TopSpeed = AcCarUi.GetString(specs, "topspeed"),
                Acceleration = AcCarUi.GetString(specs, "acceleration"),
                PwRatio = AcCarUi.GetString(specs, "pwratio")
            },
            TorqueCurve = Curve(ui["torqueCurve"]),
            PowerCurve = Curve(ui["powerCurve"]),
            Country = AcCarUi.GetString(ui, "country"),
            Author = AcCarUi.GetString(ui, "author"),
            Year = AcCarUi.GetInt(ui, "year"),
            Version = AcCarUi.GetString(ui, "version"),
            Url = AcCarUi.GetString(ui, "url")
        };
    }

    /// <summary>A curve as [[rpm, value], ...]; points that are not pairs of numbers are left out; null when there is none</summary>
    private static List<List<double>>? Curve(JToken? token)
    {
        if (token is not JArray points) return null;

        var curve = new List<List<double>>();
        foreach (var point in points.OfType<JArray>())
        {
            var values = point.OfType<JValue>()
                .Where(v => v.Type is JTokenType.Integer or JTokenType.Float)
                .Select(v => Convert.ToDouble(v.Value, System.Globalization.CultureInfo.InvariantCulture))
                .ToList();
            if (values.Count >= 2) curve.Add(values);
        }

        return curve;
    }

    public async Task<List<TrackInfo>> LoadTracksAsync()
    {
        _tracks.Clear();

        if (!Directory.Exists(_settings.TracksPath))
        {
            var error = $"Tracks directory not found: {_settings.TracksPath}";
            _logger.Error("Tracks directory not found: {TracksPath}", _settings.TracksPath);
            throw new DirectoryNotFoundException(error);
        }

        // Recursively find all ui_track.json files
        var uiTrackFiles = Directory.GetFiles(_settings.TracksPath, "ui_track.json", SearchOption.AllDirectories);
        _logger.Information("Found {Count} ui_track.json files in {TracksPath}", uiTrackFiles.Length, _settings.TracksPath);

        // Group files by track root folder to avoid loading variants as separate tracks
        var trackGroups = uiTrackFiles
            .Select(path => new { Path = path, TrackRoot = GetTrackRootFolder(path) })
            .Where(x => x.TrackRoot != null)
            .GroupBy(x => x.TrackRoot);

        foreach (var trackGroup in trackGroups)
        {
            try
            {
                var trackFolder = trackGroup.Key!;
                var trackId = Path.GetFileName(trackFolder);

                // Find the main ui_track.json (directly under trackFolder/ui/)
                var mainJsonPath = Path.Combine(trackFolder, "ui", "ui_track.json");
                string jsonPathToLoad;

                if (File.Exists(mainJsonPath))
                {
                    jsonPathToLoad = mainJsonPath;
                }
                else
                {
                    // If no main ui_track.json, use the first variant
                    jsonPathToLoad = trackGroup.First().Path;
                }

                var jsonContent = await File.ReadAllTextAsync(jsonPathToLoad);
                var trackInfo = JsonSerializer.Deserialize<TrackInfo>(jsonContent, JsonOptions);

                if (trackInfo != null)
                {
                    trackInfo.TrackId = trackId;
                    trackInfo.FolderPath = trackFolder;

                    // Load track configurations (variants)
                    LoadTrackConfigurations(trackInfo, trackFolder);

                    _tracks.Add(trackInfo);
                    _logger.Debug("Loaded track: {Name} ({TrackId}) from {Folder}", trackInfo.Name, trackId, trackFolder);
                }
            }
            catch (Exception ex)
            {
                // Log and continue - don't fail entire load for one bad track
                _logger.Warning(ex, "Error loading track from {Folder}", trackGroup.Key);
            }
        }

        return _tracks;
    }

    private string? GetTrackRootFolder(string uiTrackJsonPath)
    {
        // ui_track.json can be at:
        // - trackRoot/ui/ui_track.json (main track)
        // - trackRoot/ui/variant/ui_track.json (variant)

        var uiFolder = Path.GetDirectoryName(uiTrackJsonPath);
        if (uiFolder == null) return null;

        var possibleTrackRoot = Path.GetDirectoryName(uiFolder);
        if (possibleTrackRoot == null) return null;

        // Check if parent of ui folder is in the tracks directory
        if (Path.GetDirectoryName(possibleTrackRoot) == _settings.TracksPath)
        {
            return possibleTrackRoot;
        }

        // Otherwise, it's a variant - go one more level up
        var parentOfPossibleRoot = Path.GetDirectoryName(possibleTrackRoot);
        if (parentOfPossibleRoot != null && Path.GetDirectoryName(parentOfPossibleRoot) == _settings.TracksPath)
        {
            return parentOfPossibleRoot;
        }

        return null;
    }

    private void LoadTrackConfigurations(TrackInfo trackInfo, string trackFolder)
    {
        // Check for track configurations in the ui folder
        var uiFolder = Path.Combine(trackFolder, "ui");

        if (!Directory.Exists(uiFolder))
            return;

        // Look for layout-specific JSON files (e.g., ui_gp.json, ui_national.json)
        var configFiles = Directory.GetFiles(uiFolder, "ui_*.json")
            .Where(f => !f.EndsWith("ui_track.json", StringComparison.OrdinalIgnoreCase));

        foreach (var configFile in configFiles)
        {
            try
            {
                var configName = Path.GetFileNameWithoutExtension(configFile)
                    .Replace("ui_", "", StringComparison.OrdinalIgnoreCase);

                var jsonContent = File.ReadAllText(configFile);
                var configData = JsonSerializer.Deserialize<TrackConfiguration>(jsonContent, JsonOptions);

                if (configData != null)
                {
                    configData.FolderName = configName;
                    trackInfo.Configurations.Add(configData);
                }
            }
            catch
            {
                // Skip invalid config files
            }
        }

        // Look for variant subfolders (e.g., ui/drag200/ui_track.json, ui/drag400/ui_track.json)
        foreach (var subDir in Directory.GetDirectories(uiFolder))
        {
            var variantJsonPath = Path.Combine(subDir, "ui_track.json");
            if (!File.Exists(variantJsonPath))
                continue;

            try
            {
                var folderName = Path.GetFileName(subDir);
                var jsonContent = File.ReadAllText(variantJsonPath);
                var configData = JsonSerializer.Deserialize<TrackConfiguration>(jsonContent, JsonOptions);

                if (configData != null)
                {
                    configData.FolderName = folderName;
                    trackInfo.Configurations.Add(configData);
                }
            }
            catch
            {
                // Skip invalid variant folders
            }
        }
    }

    public List<CarInfo> GetCars() => _cars;

    public List<TrackInfo> GetTracks() => _tracks;

    public async Task ReloadContentAsync()
    {
        await LoadCarsAsync();
        await LoadTracksAsync();
    }

    public string? GetTrackPreviewPath(string trackId)
    {
        if (string.IsNullOrEmpty(_settings.TracksPath))
            return null;

        // AC stores track previews at: content/tracks/{trackId}/ui/preview.png
        var previewPath = Path.Combine(_settings.TracksPath, trackId, "ui", "preview.png");

        if (File.Exists(previewPath))
            return previewPath;

        // Some tracks use outline.png as fallback
        var outlinePath = Path.Combine(_settings.TracksPath, trackId, "ui", "outline.png");

        if (File.Exists(outlinePath))
            return outlinePath;

        return null;
    }
}
