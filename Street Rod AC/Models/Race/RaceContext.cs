namespace Street_Rod_AC.Models.Race
{
    /// <summary>
    /// Race context containing pre-race setup information
    /// Used to correlate race results with game state updates
    /// Stored in LaunchIntent.Metadata and passed to ingestion service
    /// </summary>
    public class RaceContext
    {
        /// <summary>
        /// Unique identifier for this race context
        /// </summary>
        public Guid ContextId { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Player's racer name
        /// </summary>
        public string PlayerName { get; set; } = string.Empty;

        /// <summary>
        /// Opponent racer name
        /// </summary>
        public string OpponentName { get; set; } = string.Empty;

        /// <summary>
        /// Player's car instance ID
        /// </summary>
        public Guid PlayerCarInstanceId { get; set; }

        /// <summary>
        /// Opponent's car instance ID
        /// </summary>
        public Guid OpponentCarInstanceId { get; set; }

        /// <summary>
        /// Cash wager amount (0 if no wager)
        /// </summary>
        public decimal CashWager { get; set; }

        /// <summary>
        /// Whether this is a pink slip race (loser gives up their car)
        /// </summary>
        public bool IsPinkSlip { get; set; }

        /// <summary>
        /// Track identifier
        /// </summary>
        public string TrackId { get; set; } = string.Empty;

        /// <summary>
        /// Race type (drag, circuit, etc.)
        /// </summary>
        public RaceType RaceType { get; set; }

        /// <summary>
        /// When this context was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Event ID if this is an event race (null for regular races)
        /// </summary>
        public string? EventId { get; set; }

        /// <summary>
        /// True if opponent is event-only (don't track their stats)
        /// </summary>
        public bool IsEventOnlyOpponent { get; set; }
    }

    /// <summary>
    /// Type of race being run
    /// </summary>
    public enum RaceType
    {
        /// <summary>
        /// Quarter-mile drag race (1v1)
        /// </summary>
        DragRace,

        /// <summary>
        /// Circuit race (multiple laps)
        /// </summary>
        Circuit,

        /// <summary>
        /// Point-to-point sprint
        /// </summary>
        Sprint
    }
}
