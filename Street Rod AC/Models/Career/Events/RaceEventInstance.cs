using Street_Rod_AC.Models.Race;

namespace Street_Rod_AC.Models.Career.Events
{
    /// <summary>
    /// Represents an active instance of a race event.
    /// Created when an event is generated/scheduled, tracks completion state.
    /// </summary>
    public class RaceEventInstance
    {
        /// <summary>
        /// Unique ID for this event instance
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
        /// When this event expires (null = no expiration)
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
        /// Track used for this event instance
        /// </summary>
        public string? TrackId { get; set; }

        /// <summary>
        /// Opponent assigned to this event instance
        /// </summary>
        public string? OpponentName { get; set; }

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

        /// <summary>
        /// Mark the event as completed
        /// </summary>
        public void Complete(bool playerWon, DateTime completedAt)
        {
            IsCompleted = true;
            PlayerWon = playerWon;
            CompletedAt = completedAt;
        }
    }
}
