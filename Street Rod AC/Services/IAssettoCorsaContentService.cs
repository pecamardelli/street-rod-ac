using Street_Rod_AC.Models.AC;

namespace Street_Rod_AC.Services;

/// <summary>
/// Service for discovering and loading Assetto Corsa content (cars, tracks)
/// </summary>
public interface IAssettoCorsaContentService
{
    /// <summary>
    /// Loads all available cars from the AC content folder
    /// </summary>
    Task<List<CarInfo>> LoadCarsAsync();

    /// <summary>
    /// Loads all available tracks from the AC content folder
    /// </summary>
    Task<List<TrackInfo>> LoadTracksAsync();

    /// <summary>
    /// Gets all loaded cars (cached)
    /// </summary>
    List<CarInfo> GetCars();

    /// <summary>
    /// Gets all loaded tracks (cached)
    /// </summary>
    List<TrackInfo> GetTracks();

    /// <summary>
    /// Reloads all content from disk
    /// </summary>
    Task ReloadContentAsync();
}
