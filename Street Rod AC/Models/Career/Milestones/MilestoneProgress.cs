namespace Street_Rod_AC.Models.Career.Milestones
{
    /// <summary>
    /// Represents the current progress toward a milestone
    /// </summary>
    public class MilestoneProgress
    {
        /// <summary>
        /// The milestone definition
        /// </summary>
        public MilestoneDefinition Definition { get; set; } = new();

        /// <summary>
        /// Current value toward the target
        /// </summary>
        public int CurrentValue { get; set; }

        /// <summary>
        /// Target value needed for completion
        /// </summary>
        public int TargetValue => Definition.TargetValue;

        /// <summary>
        /// Progress percentage (0-100)
        /// </summary>
        public float Percentage => TargetValue > 0
            ? Math.Min(100f, (CurrentValue / (float)TargetValue) * 100f)
            : 0f;

        /// <summary>
        /// Whether this milestone has been completed
        /// </summary>
        public bool IsCompleted { get; set; }

        /// <summary>
        /// When this milestone was completed (if completed)
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Get a human-readable progress string
        /// </summary>
        public string ProgressText => IsCompleted
            ? "Completed!"
            : $"{CurrentValue}/{TargetValue}";
    }
}
