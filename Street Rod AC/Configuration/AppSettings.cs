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
    /// Path to the converted part packs: the game's own content, shipped next to it (see tools/convert-parts.ps1)
    /// </summary>
    public string PartsPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Parts");

    /// <summary>The engine sound library: a folder per sound, next to the parts</summary>
    public string SoundsPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Sounds");

    /// <summary>The install's master GUIDs, where Kunos cars name their sound events</summary>
    public string SfxGuidsPath => Path.Combine(AssettoCorsaPath, "content", "sfx", "GUIDs.txt");

    /// <summary>
    /// What a part costs against the value its script gives it. The scripts think in the dollars of the early
    /// 2000s, the game's cars are priced in those of 1970: about a fifth.
    /// </summary>
    public double PartsPriceScale { get; set; } = 0.2;

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
