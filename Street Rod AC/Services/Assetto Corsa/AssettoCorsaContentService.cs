using System.IO;
using System.Linq;
using System.Text.Json;
using Street_Rod_AC.Configuration;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.AC;

namespace Street_Rod_AC.Services;

/// <summary>
/// Service for discovering and loading Assetto Corsa content. The cars are read by the catalog's import
/// (<see cref="Catalog.CarImportService"/>); this service reads the tracks.
/// </summary>
public class AssettoCorsaContentService : IAssettoCorsaContentService
{
    private const string UiFolder = "ui";
    private const string TrackJson = "ui_track.json";

    private readonly AppSettings _settings;
    private List<TrackInfo> _tracks = [];
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

    public async Task<List<TrackInfo>> LoadTracksAsync()
    {
        var tracksPath = _settings.TracksPath;
        if (!Directory.Exists(tracksPath))
        {
            var error = $"Tracks directory not found: {tracksPath}";
            _logger.Error("Tracks directory not found: {TracksPath}", tracksPath);
            throw new DirectoryNotFoundException(error);
        }

        // On a worker: a modded install has hundreds of tracks, and this runs during start-up
        _tracks = await Task.Run(() => LoadTracks(tracksPath));
        return _tracks;
    }

    private List<TrackInfo> LoadTracks(string tracksPath)
    {
        var tracks = new List<TrackInfo>();
        var trackFolders = Directory.GetDirectories(tracksPath);
        var withJson = 0;

        foreach (var trackFolder in trackFolders)
        {
            try
            {
                // Only where AC keeps it: ui\ui_track.json for the track, ui\<layout>\ui_track.json for its layouts.
                // A track with no main file is read from its first layout's.
                var jsonPathToLoad = MainTrackJson(trackFolder);
                if (jsonPathToLoad == null) continue;
                withJson++;

                var trackId = Path.GetFileName(trackFolder);
                var trackInfo = JsonSerializer.Deserialize<TrackInfo>(File.ReadAllText(jsonPathToLoad), JsonOptions);

                if (trackInfo != null)
                {
                    trackInfo.TrackId = trackId;
                    trackInfo.FolderPath = trackFolder;

                    // Load track configurations (variants)
                    LoadTrackConfigurations(trackInfo, trackFolder);

                    tracks.Add(trackInfo);
                    _logger.Debug("Loaded track: {Name} ({TrackId}) from {Folder}", trackInfo.Name, trackId, trackFolder);
                }
            }
            catch (Exception ex)
            {
                // Log and continue - don't fail entire load for one bad track
                _logger.Warning(ex, "Error loading track from {Folder}", trackFolder);
            }
        }

        _logger.Information("Found {Count} tracks with a ui_track.json in {TracksPath}", withJson, tracksPath);
        return tracks;
    }

    /// <summary>The track's own ui_track.json, else its first layout's; null when it has neither</summary>
    private static string? MainTrackJson(string trackFolder)
    {
        var uiFolder = Path.Combine(trackFolder, UiFolder);
        if (!Directory.Exists(uiFolder)) return null;

        var main = Path.Combine(uiFolder, TrackJson);
        if (File.Exists(main)) return main;

        return Directory.EnumerateDirectories(uiFolder)
            .Select(layout => Path.Combine(layout, TrackJson))
            .FirstOrDefault(File.Exists);
    }

    private void LoadTrackConfigurations(TrackInfo trackInfo, string trackFolder)
    {
        // Check for track configurations in the ui folder
        var uiFolder = Path.Combine(trackFolder, UiFolder);

        if (!Directory.Exists(uiFolder))
            return;

        // Look for layout-specific JSON files (e.g., ui_gp.json, ui_national.json)
        var configFiles = Directory.GetFiles(uiFolder, "ui_*.json")
            .Where(f => !f.EndsWith(TrackJson, StringComparison.OrdinalIgnoreCase));

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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _logger.Warning(ex, "Track {TrackId}: layout file {File} skipped", trackInfo.TrackId, configFile);
            }
        }

        // Look for variant subfolders (e.g., ui/drag200/ui_track.json, ui/drag400/ui_track.json)
        foreach (var subDir in Directory.GetDirectories(uiFolder))
        {
            var variantJsonPath = Path.Combine(subDir, TrackJson);
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _logger.Warning(ex, "Track {TrackId}: layout {File} skipped", trackInfo.TrackId, variantJsonPath);
            }
        }
    }

    public List<TrackInfo> GetTracks() => _tracks;

    public string? GetTrackPreviewPath(string trackId)
    {
        if (string.IsNullOrEmpty(_settings.TracksPath))
            return null;

        // AC stores track previews at: content/tracks/{trackId}/ui/preview.png
        var previewPath = Path.Combine(_settings.TracksPath, trackId, UiFolder, "preview.png");

        if (File.Exists(previewPath))
            return previewPath;

        // Some tracks use outline.png as fallback
        var outlinePath = Path.Combine(_settings.TracksPath, trackId, UiFolder, "outline.png");

        if (File.Exists(outlinePath))
            return outlinePath;

        return null;
    }
}
