using LiteDB;

namespace Street_Rod_AC.Models.Race
{
    /// <summary>
    /// Persisted race session data for historical tracking and deduplication
    /// Stored in LiteDB in the save file database
    /// Collection: "ProcessedRaceSessions"
    /// </summary>
    public class ProcessedRaceSession
    {
        /// <summary>
        /// Session ID from Lua app (UUID) - used as primary key
        /// This is the authoritative unique identifier for deduplication
        /// </summary>
        [BsonId]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// When this session was processed by the C# application
        /// </summary>
        public DateTime ProcessedAt { get; set; }

        /// <summary>
        /// Race context ID (if available from launch intent)
        /// </summary>
        public Guid? RaceContextId { get; set; }

        /// <summary>
        /// Winner's driver name
        /// </summary>
        public string WinnerName { get; set; } = string.Empty;

        /// <summary>
        /// Loser's driver name
        /// </summary>
        public string LoserName { get; set; } = string.Empty;

        /// <summary>
        /// Whether the player won this race
        /// </summary>
        public bool PlayerWon { get; set; }

        /// <summary>
        /// Cash wager amount (0 if no wager)
        /// </summary>
        public decimal CashWager { get; set; }

        /// <summary>
        /// Whether this was a pink slip race
        /// </summary>
        public bool WasPinkSlip { get; set; }

        /// <summary>
        /// Track identifier from AC
        /// </summary>
        public string TrackId { get; set; } = string.Empty;

        /// <summary>
        /// Full race result JSON (stored as string for archival purposes)
        /// Can be used for debugging or reprocessing
        /// </summary>
        public string RawResultJson { get; set; } = string.Empty;

        /// <summary>
        /// Session start timestamp from Lua app
        /// </summary>
        public DateTime SessionStartTime { get; set; }

        /// <summary>
        /// Race duration in seconds
        /// </summary>
        public double DurationSeconds { get; set; }

        /// <summary>
        /// How the race was decided
        /// </summary>
        public string WinCondition { get; set; } = string.Empty;

        // Who raced whom in what, and when in the game. Null or empty on sessions from before they were kept.

        /// <summary>The game's date when the race was settled (1970s)</summary>
        public DateTime? GameDate { get; set; }

        public string PlayerName { get; set; } = string.Empty;

        public string OpponentName { get; set; } = string.Empty;

        public Guid? PlayerCarInstanceId { get; set; }

        public Guid? OpponentCarInstanceId { get; set; }

        /// <summary>The <see cref="Race.RaceType"/> by name</summary>
        public string RaceType { get; set; } = string.Empty;

        /// <summary>The event the race was for; null for a race at the diner</summary>
        public string? EventId { get; set; }
    }
}
