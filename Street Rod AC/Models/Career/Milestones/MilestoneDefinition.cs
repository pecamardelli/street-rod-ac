namespace Street_Rod_AC.Models.Career.Milestones
{
    /// <summary>
    /// Defines a milestone achievement that can be completed by the player.
    /// Milestones unlock victories, events, and other content.
    /// </summary>
    public class MilestoneDefinition
    {
        /// <summary>
        /// Unique identifier for this milestone
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Display name of the milestone
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Description of what this milestone represents
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// The type of event that triggers progress toward this milestone
        /// </summary>
        public MilestoneTrigger Trigger { get; set; }

        /// <summary>
        /// The target value needed to complete this milestone
        /// </summary>
        public int TargetValue { get; set; }

        /// <summary>
        /// IDs of content unlocked when this milestone is completed
        /// (Victory IDs, Event IDs, etc.)
        /// </summary>
        public List<string> Unlocks { get; set; } = [];

        /// <summary>
        /// Optional category for grouping milestones in UI
        /// </summary>
        public string? Category { get; set; }

        /// <summary>
        /// Whether this milestone is hidden until completed
        /// </summary>
        public bool IsSecret { get; set; }

        /// <summary>
        /// Create a new milestone definition
        /// </summary>
        public MilestoneDefinition() { }

        /// <summary>
        /// Create a new milestone definition with all parameters
        /// </summary>
        public MilestoneDefinition(string id, string name, string description,
            MilestoneTrigger trigger, int targetValue, params string[] unlocks)
        {
            Id = id;
            Name = name;
            Description = description;
            Trigger = trigger;
            TargetValue = targetValue;
            Unlocks = [.. unlocks];
        }
    }
}
