using Street_Rod_AC.Models.AC;

namespace Street_Rod_AC.Services;

/// <summary>
/// Service for launching Assetto Corsa with specific car and track configurations
/// </summary>
public interface IAssettoCorsaLauncher
{
    /// <summary>
    /// Launches Assetto Corsa with the specified car and track, and waits for it to exit
    /// </summary>
    /// <param name="car">The car to use</param>
    /// <param name="track">The track to use</param>
    /// <param name="trackConfig">Optional track configuration/variant (e.g., "drag1000")</param>
    Task LaunchRaceAsync(CarInfo car, TrackInfo track, string? trackConfig = null);
}
