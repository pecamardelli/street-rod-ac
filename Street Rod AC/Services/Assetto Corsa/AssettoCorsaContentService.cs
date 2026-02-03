using System.IO;
using System.Linq;
using System.Text.Json;
using Street_Rod_AC.Configuration;
using Street_Rod_AC.Models.AC;

namespace Street_Rod_AC.Services;

/// <summary>
/// Service for discovering and loading Assetto Corsa content
/// </summary>
public class AssettoCorsaContentService : IAssettoCorsaContentService
{
    private readonly AppSettings _settings;
    private readonly List<CarInfo> _cars = [];
    private readonly List<TrackInfo> _tracks = [];

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
            Console.WriteLine(error);
            throw new DirectoryNotFoundException(error);
        }

        // Recursively find all ui_car.json files
        var uiCarFiles = Directory.GetFiles(_settings.CarsPath, "ui_car.json", SearchOption.AllDirectories);
        Console.WriteLine($"Found {uiCarFiles.Length} ui_car.json files in {_settings.CarsPath}");

        foreach (var uiJsonPath in uiCarFiles)
        {
            try
            {
                // Find the car root folder (parent of the ui folder)
                var uiFolder = Path.GetDirectoryName(uiJsonPath);
                var carFolder = Path.GetDirectoryName(uiFolder);

                if (carFolder == null || uiFolder == null)
                    continue;

                // Use the folder name as car ID
                var carId = Path.GetFileName(carFolder);

                var jsonContent = await File.ReadAllTextAsync(uiJsonPath);
                var carInfo = JsonSerializer.Deserialize<CarInfo>(jsonContent, JsonOptions);

                if (carInfo != null)
                {
                    carInfo.CarId = carId;
                    carInfo.FolderPath = carFolder;
                    _cars.Add(carInfo);
                    Console.WriteLine($"Loaded car: {carInfo.Name} ({carId}) from {carFolder}");
                }
            }
            catch (Exception ex)
            {
                // Log and continue - don't fail entire load for one bad car
                Console.WriteLine($"Error loading car from {uiJsonPath}: {ex.Message}");
            }
        }

        return _cars;
    }

    public async Task<List<TrackInfo>> LoadTracksAsync()
    {
        _tracks.Clear();

        if (!Directory.Exists(_settings.TracksPath))
        {
            var error = $"Tracks directory not found: {_settings.TracksPath}";
            Console.WriteLine(error);
            throw new DirectoryNotFoundException(error);
        }

        // Recursively find all ui_track.json files
        var uiTrackFiles = Directory.GetFiles(_settings.TracksPath, "ui_track.json", SearchOption.AllDirectories);
        Console.WriteLine($"Found {uiTrackFiles.Length} ui_track.json files in {_settings.TracksPath}");

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
                    Console.WriteLine($"Loaded track: {trackInfo.Name} ({trackId}) from {trackFolder}");
                }
            }
            catch (Exception ex)
            {
                // Log and continue - don't fail entire load for one bad track
                Console.WriteLine($"Error loading track from {trackGroup.Key}: {ex.Message}");
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
}
