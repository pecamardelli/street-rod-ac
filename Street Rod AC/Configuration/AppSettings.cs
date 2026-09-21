using System.IO;

namespace Street_Rod_AC.Configuration;

/// <summary>
/// Application configuration settings
/// </summary>
public class AppSettings
{
    private static AppSettings? _instance;
    private static readonly object _lock = new();

    /// <summary>
    /// Singleton instance of AppSettings
    /// </summary>
    public static AppSettings Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new AppSettings();
                }
            }
            return _instance;
        }
    }

    private AppSettings()
    {
        // For now, hardcode the AC path as specified
        AssettoCorsaPath = @"C:\GAMES\Street Rod AC";
    }

    /// <summary>
    /// Path to the Assetto Corsa installation directory
    /// </summary>
    public string AssettoCorsaPath { get; set; }

    /// <summary>
    /// Path to the cars content folder
    /// </summary>
    public string CarsPath => Path.Combine(AssettoCorsaPath, "content", "cars");

    /// <summary>
    /// Path to the tracks content folder
    /// </summary>
    public string TracksPath => Path.Combine(AssettoCorsaPath, "content", "tracks");

    /// <summary>
    /// Path to the showrooms content folder
    /// </summary>
    public string ShowroomsPath => Path.Combine(AssettoCorsaPath, "content", "showroom");

    /// <summary>
    /// Showroom used as the 3D garage environment
    /// </summary>
    public string GarageShowroomId { get; set; } = "Hangar";

    /// <summary>
    /// Full path to the garage showroom model
    /// </summary>
    public string GarageShowroomKn5 => Path.Combine(ShowroomsPath, GarageShowroomId, GarageShowroomId + ".kn5");

    /// <summary>
    /// Path to the converted part packs (see tools/SlrrPartsConverter)
    /// </summary>
    public string PartsPath => Path.Combine(AssettoCorsaPath, "content", "parts");

    /// <summary>
    /// Engine shown in the garage until cars carry their own: id of the engine block part
    /// </summary>
    public string GarageEnginePart { get; set; } = "engines/Mopar/block_340";

    /// <summary>
    /// Validates that the AC installation path exists and is valid
    /// </summary>
    public bool IsValidInstallation()
    {
        if (string.IsNullOrEmpty(AssettoCorsaPath) || !Directory.Exists(AssettoCorsaPath))
            return false;

        return Directory.Exists(CarsPath) && Directory.Exists(TracksPath);
    }
}
