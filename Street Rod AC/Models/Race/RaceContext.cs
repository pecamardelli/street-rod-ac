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
        /// Track configuration/layout (e.g., "drag1000", "gp")
        /// </summary>
        public string? TrackConfig { get; set; }

        /// <summary>
        /// Race type (drag, circuit, etc.)
        /// </summary>
        public RaceType RaceType { get; set; }

        /// <summary>
        /// When this context was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// When Assetto Corsa was started for this race; null while it is being prepared, and for a race whose
        /// launch never got that far. Only a race that ran can be forfeited for bringing back no result: a pending
        /// race without it (the app died before AC started) is simply released.
        /// </summary>
        public DateTime? LaunchedAt { get; set; }

        /// <summary>
        /// Event ID if this is an event race (null for regular races)
        /// </summary>
        public string? EventId { get; set; }

        /// <summary>
        /// The instance of the event this race is for (<c>RaceEventState.InstanceId</c>). Daily and weekly
        /// events share their definition id, so this is what tells them apart when the race completes one;
        /// null for regular races and for contexts saved before instances had ids.
        /// </summary>
        public Guid? EventInstanceId { get; set; }

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
