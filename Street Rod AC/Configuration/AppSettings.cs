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
    /// Validates that the AC installation path exists and is valid
    /// </summary>
    public bool IsValidInstallation()
    {
        if (string.IsNullOrEmpty(AssettoCorsaPath) || !Directory.Exists(AssettoCorsaPath))
            return false;

        return Directory.Exists(CarsPath) && Directory.Exists(TracksPath);
    }
}
