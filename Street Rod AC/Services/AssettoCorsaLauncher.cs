using System.Diagnostics;
using System.IO;
using System.Text;
using Street_Rod_AC.Configuration;
using Street_Rod_AC.Models.AC;

namespace Street_Rod_AC.Services;

/// <summary>
/// Service for launching Assetto Corsa with specific car and track configurations
/// </summary>
public class AssettoCorsaLauncher : IAssettoCorsaLauncher
{
    private readonly AppSettings _settings;

    public AssettoCorsaLauncher()
    {
        _settings = AppSettings.Instance;
    }

    public async Task LaunchRaceAsync(CarInfo car, TrackInfo track, string? trackConfig = null)
    {
        ArgumentNullException.ThrowIfNull(car);

        ArgumentNullException.ThrowIfNull(track);

        // Update race.ini with selected car and track
        UpdateRaceIni(car, track, trackConfig);

        // Launch acs.exe and wait for it to exit
        await LaunchGameAsync();
    }

    private void UpdateRaceIni(CarInfo car, TrackInfo track, string? trackConfig)
    {
        var raceIniPath = Path.Combine(_settings.AssettoCorsaPath, "cfg", "race.ini");

        if (!File.Exists(raceIniPath))
        {
            throw new FileNotFoundException($"race.ini not found at: {raceIniPath}");
        }

        // Read the existing race.ini
        var lines = File.ReadAllLines(raceIniPath);
        var updatedLines = new List<string>();
        bool inRaceSection = false;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // Track which section we're in
            if (trimmedLine.StartsWith("[RACE]", StringComparison.OrdinalIgnoreCase))
            {
                inRaceSection = true;
                updatedLines.Add(line);
                continue;
            }
            else if (trimmedLine.StartsWith("[", StringComparison.OrdinalIgnoreCase))
            {
                inRaceSection = false;
            }

            // Update values in RACE section
            if (inRaceSection)
            {
                if (trimmedLine.StartsWith("TRACK=", StringComparison.OrdinalIgnoreCase))
                {
                    updatedLines.Add($"TRACK={track.TrackId}");
                    continue;
                }
                else if (trimmedLine.StartsWith("CONFIG_TRACK=", StringComparison.OrdinalIgnoreCase))
                {
                    updatedLines.Add($"CONFIG_TRACK={trackConfig ?? ""}");
                    continue;
                }
                else if (trimmedLine.StartsWith("MODEL=", StringComparison.OrdinalIgnoreCase))
                {
                    updatedLines.Add($"MODEL={car.CarId}");
                    continue;
                }
            }

            // Keep all other lines unchanged
            updatedLines.Add(line);
        }

        // Write the updated race.ini
        File.WriteAllLines(raceIniPath, updatedLines, Encoding.UTF8);
        Console.WriteLine($"Updated race.ini: Track={track.TrackId}, Config={trackConfig}, Car={car.CarId}");
    }

    private async Task LaunchGameAsync()
    {
        var acsExePath = Path.Combine(_settings.AssettoCorsaPath, "acs.exe");

        if (!File.Exists(acsExePath))
        {
            throw new FileNotFoundException($"acs.exe not found at: {acsExePath}");
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = acsExePath,
                WorkingDirectory = _settings.AssettoCorsaPath,
                UseShellExecute = true
            };

            var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start Assetto Corsa process");
            }

            Console.WriteLine($"Launched Assetto Corsa from: {acsExePath}");
            Console.WriteLine("Waiting for Assetto Corsa to exit...");

            // Wait for the game to exit
            await process.WaitForExitAsync();

            Console.WriteLine("Assetto Corsa has exited");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to launch Assetto Corsa: {ex.Message}", ex);
        }
    }
}
