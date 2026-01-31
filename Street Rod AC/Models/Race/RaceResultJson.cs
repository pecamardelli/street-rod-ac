using Newtonsoft.Json;

namespace Street_Rod_AC.Models.Race
{
    /// <summary>
    /// Root schema for race result JSON files from sr_race_manager (Lua app)
    /// Mirrors the Lua app output structure for deserialization
    /// </summary>
    public class RaceResultJson
    {
        [JsonProperty("metadata")]
        public RaceMetadata Metadata { get; set; } = new();

        [JsonProperty("session")]
        public RaceSession Session { get; set; } = new();

        [JsonProperty("participants")]
        public List<RaceParticipant> Participants { get; set; } = new();
    }

    /// <summary>
    /// Metadata about the race result file
    /// Used for validation and version tracking
    /// </summary>
    public class RaceMetadata
    {
        [JsonProperty("schema_version")]
        public string SchemaVersion { get; set; } = string.Empty;

        [JsonProperty("script_version")]
        public string ScriptVersion { get; set; } = string.Empty;

        [JsonProperty("source")]
        public string Source { get; set; } = string.Empty;  // Must be "sr_race_manager"

        [JsonProperty("generated_at")]
        public string GeneratedAt { get; set; } = string.Empty;
    }

    /// <summary>
    /// Race session information
    /// Contains timing, track, and session identification data
    /// </summary>
    public class RaceSession
    {
        [JsonProperty("session_id")]
        public string SessionId { get; set; } = string.Empty;  // Primary deduplication key (UUID)

        [JsonProperty("start_timestamp")]
        public string StartTimestamp { get; set; } = string.Empty;

        [JsonProperty("end_timestamp")]
        public string EndTimestamp { get; set; } = string.Empty;

        [JsonProperty("duration_seconds")]
        public double DurationSeconds { get; set; }

        [JsonProperty("track_id")]
        public string TrackId { get; set; } = string.Empty;

        [JsonProperty("track_layout")]
        public string? TrackLayout { get; set; }
    }

    /// <summary>
    /// Individual participant (driver) data in the race
    /// Contains performance metrics and crash data
    /// </summary>
    public class RaceParticipant
    {
        [JsonProperty("driver_name")]
        public string DriverName { get; set; } = string.Empty;

        [JsonProperty("car_name")]
        public string CarName { get; set; } = string.Empty;

        [JsonProperty("performance")]
        public ParticipantPerformance Performance { get; set; } = new();

        [JsonProperty("crash")]
        public CrashData Crash { get; set; } = new();
    }

    /// <summary>
    /// Performance metrics for a participant
    /// Includes lap times, speed, distance, and fuel data
    /// </summary>
    public class ParticipantPerformance
    {
        [JsonProperty("final_position")]
        public int? FinalPosition { get; set; }

        [JsonProperty("laps_completed")]
        public int LapsCompleted { get; set; }

        [JsonProperty("best_lap_time_ms")]
        public double? BestLapTimeMs { get; set; }

        [JsonProperty("total_race_time_ms")]
        public double TotalRaceTimeMs { get; set; }

        [JsonProperty("max_speed_kmh")]
        public double MaxSpeedKmh { get; set; }

        [JsonProperty("distance_km")]
        public double DistanceKm { get; set; }

        [JsonProperty("fuel_consumed_liters")]
        public double FuelConsumedLiters { get; set; }
    }

    /// <summary>
    /// Crash detection data for a participant
    /// Tracks crash status, intensity, and timing
    /// </summary>
    public class CrashData
    {
        [JsonProperty("crashed")]
        public bool Crashed { get; set; }

        [JsonProperty("crash_intensities_g")]
        public List<double> CrashIntensitiesG { get; set; } = new();

        [JsonProperty("max_crash_intensity_g")]
        public double MaxCrashIntensityG { get; set; }

        [JsonProperty("crash_timestamp")]
        public string? CrashTimestamp { get; set; }
    }
}
