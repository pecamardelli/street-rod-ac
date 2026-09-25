using Street_Rod_AC.Models.Career.Milestones;

namespace Street_Rod_AC.Models.GameState
{
    /// <summary>
    /// Tracks career progression including victories, milestones, and events.
    /// Part of the main GameState.
    /// </summary>
    public class CareerState
    {
        #region Victory Tracking

        /// <summary>
        /// The victory type the player is currently pursuing (if any)
        /// </summary>
        public string? ActiveVictoryType { get; set; }

        /// <summary>
        /// Victory types that have been unlocked and are available to pursue
        /// </summary>
        public Dictionary<string, bool> UnlockedVictories { get; set; } = [];

        /// <summary>
        /// Whether the player has won the game
        /// </summary>
        public bool HasWonGame { get; set; }

        /// <summary>
        /// The victory type that was achieved to win the game
        /// </summary>
        public string? WinningVictoryType { get; set; }

        #endregion

        #region Milestone Tracking

        /// <summary>
        /// IDs of milestones that have been completed
        /// </summary>
        public HashSet<string> CompletedMilestones { get; set; } = [];

        /// <summary>
        /// Running counters for milestone progress tracking.
        /// Updated after races and other game events.
        /// </summary>
        public Dictionary<MilestoneTrigger, int> MilestoneCounters { get; set; } = [];

        #endregion

        #region Event Tracking

        /// <summary>
        /// Currently active race events/invitations
        /// </summary>
        public List<RaceEventState> ActiveEvents { get; set; } = [];

        /// <summary>
        /// IDs of events that have been completed (for one-time events)
        /// </summary>
        public HashSet<string> CompletedEventIds { get; set; } = [];

        #endregion

        #region Opponent Tracking

        /// <summary>
        /// Names of opponents the player has defeated at least once
        /// </summary>
        public HashSet<string> DefeatedOpponentIds { get; set; } = [];

        #endregion

        #region Standing on the street

        // Worked out from the whole game by the victory service (RefreshStanding) and kept here, so that a victory's
        // progress read from the career alone (the Career screen) shows them, after a load too. Saves from before
        // them load with the defaults until the next refresh.

        /// <summary>How many racers there are to beat, for the Domination victory</summary>
        public int TotalOpponents { get; set; }

        /// <summary>Whether the player has more wins than any other racer, for the Season Champion victory</summary>
        public bool PlayerHasMostWins { get; set; }

        /// <summary>
        /// The King's name, as <see cref="DefeatedOpponentIds"/> holds the racers beaten; null when the game has no
        /// King (or has not looked yet), and the King's usual name counts
        /// </summary>
        public string? KingName { get; set; }

        #endregion

        /// <summary>
        /// Initialize default career state for a new game
        /// </summary>
        public static CareerState CreateNew()
        {
            var state = new CareerState();

            // Initialize milestone counters to zero
            foreach (MilestoneTrigger trigger in Enum.GetValues(typeof(MilestoneTrigger)))
            {
                state.MilestoneCounters[trigger] = 0;
            }

            // Reputation victory is always available from the start
            state.UnlockedVictories["Reputation"] = true;

            return state;
        }

        #region Counter Update Methods

        /// <summary>
        /// Increment a milestone counter by a specified amount
        /// </summary>
        public void IncrementCounter(MilestoneTrigger trigger, int amount = 1)
        {
            if (!MilestoneCounters.ContainsKey(trigger))
                MilestoneCounters[trigger] = 0;

            MilestoneCounters[trigger] += amount;
        }

        /// <summary>
        /// Set a milestone counter to a specific value (for "current state" counters like CarsOwned)
        /// </summary>
        public void SetCounter(MilestoneTrigger trigger, int value)
        {
            MilestoneCounters[trigger] = value;
        }

        /// <summary>
        /// Get the current value of a milestone counter
        /// </summary>
        public int GetCounter(MilestoneTrigger trigger)
        {
            return MilestoneCounters.TryGetValue(trigger, out var value) ? value : 0;
        }

        /// <summary>
        /// Brings the "current state" counters (reputation, cars owned) up to where the player stands. The events,
        /// the milestones and the victory paths read them: a new game starts at 50 reputation, not at 0.
        /// </summary>
        public void SyncStanding(Player player)
        {
            SetCounter(MilestoneTrigger.ReputationReached, player.Stats.Reputation);
            SetCounter(MilestoneTrigger.CarsOwned, player.Cars.Count);
        }

        /// <summary>
        /// Record a defeated opponent (for OpponentsDefeated milestone)
        /// </summary>
        public void RecordDefeatedOpponent(string opponentName)
        {
            if (DefeatedOpponentIds.Add(opponentName))
            {
                // Only update counter when adding a new opponent
                SetCounter(MilestoneTrigger.OpponentsDefeated, DefeatedOpponentIds.Count);
            }
        }

        #endregion
    }

    /// <summary>
    /// Represents an active race event instance
    /// </summary>
    public class RaceEventState
    {
        /// <summary>
        /// This very instance of the event, stored with it: the newspaper's invitation carries it into the race
        /// and the result completes exactly this one. (States saved before it existed get one when loaded and
        /// keep it from the next save on.)
        /// </summary>
        public Guid InstanceId { get; set; } = Guid.NewGuid();

        /// <summary>
        /// ID of the event definition this is an instance of
        /// </summary>
        public string EventDefinitionId { get; set; } = string.Empty;

        /// <summary>
        /// When this event became available
        /// </summary>
        public DateTime AvailableFrom { get; set; }

        /// <summary>
        /// When this event expires (for time-limited events)
        /// </summary>
        public DateTime? ExpiresAt { get; set; }

        /// <summary>
        /// Whether this event has been completed
        /// </summary>
        public bool IsCompleted { get; set; }

        /// <summary>
        /// Whether the player won the event (if completed)
        /// </summary>
        public bool? PlayerWon { get; set; }

        /// <summary>
        /// When the event was completed (if completed)
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Check if the event has expired
        /// </summary>
        public bool IsExpired(DateTime currentTime)
        {
            return ExpiresAt.HasValue && currentTime > ExpiresAt.Value;
        }

        /// <summary>
        /// Check if the event is currently available
        /// </summary>
        public bool IsAvailable(DateTime currentTime)
        {
            return !IsCompleted && !IsExpired(currentTime) && currentTime >= AvailableFrom;
        }
    }
}
