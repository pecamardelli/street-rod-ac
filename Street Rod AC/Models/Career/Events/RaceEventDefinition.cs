using Street_Rod_AC.Models.Career.Filters;
using Street_Rod_AC.Models.Race;

namespace Street_Rod_AC.Models.Career.Events
{
    /// <summary>
    /// Defines a race event/invitation that can appear in the game.
    /// Events have entry requirements and rewards.
    /// </summary>
    public class RaceEventDefinition
    {
        /// <summary>
        /// Unique identifier for this event type
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Display name of the event
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Description/flavor text for the event
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Type of race (drag, circuit, sprint)
        /// </summary>
        public RaceType RaceType { get; set; }

        /// <summary>
        /// Car filter for entry requirements (null = any car allowed)
        /// </summary>
        public ICarFilter? EntryRequirements { get; set; }

        /// <summary>
        /// Minimum reputation required to enter
        /// </summary>
        public int MinReputation { get; set; }

        /// <summary>
        /// Milestone IDs that must be completed to unlock this event
        /// </summary>
        public List<string> RequiredMilestones { get; set; } = [];

        /// <summary>
        /// Reward for winning the event
        /// </summary>
        public EventReward Reward { get; set; } = new();

        /// <summary>
        /// How this event is scheduled
        /// </summary>
        public EventSchedule Schedule { get; set; } = EventSchedule.Weekly;

        /// <summary>
        /// Track ID for this event (null = random appropriate track)
        /// </summary>
        public string? TrackId { get; set; }

        /// <summary>
        /// Optional opponent name for special event matchups
        /// </summary>
        public string? SpecificOpponent { get; set; }

        /// <summary>
        /// Whether this is a pink slip event
        /// </summary>
        public bool IsPinkSlip { get; set; }

        /// <summary>
        /// Get a human-readable entry requirements description
        /// </summary>
        public string GetEntryRequirementsDescription()
        {
            var requirements = new List<string>();

            if (EntryRequirements != null)
                requirements.Add(EntryRequirements.DisplayDescription);

            if (MinReputation > 0)
                requirements.Add($"{MinReputation}+ reputation");

            if (RequiredMilestones.Count > 0)
                requirements.Add($"Requires: {string.Join(", ", RequiredMilestones)}");

            return requirements.Count > 0
                ? string.Join(", ", requirements)
                : "Open to all";
        }
    }

    /// <summary>
    /// How often an event can appear
    /// </summary>
    public enum EventSchedule
    {
        /// <summary>
        /// Appears once and can only be completed once
        /// </summary>
        OneTime,

        /// <summary>
        /// Can appear once per day
        /// </summary>
        Daily,

        /// <summary>
        /// Can appear once per week
        /// </summary>
        Weekly,

        /// <summary>
        /// Always available
        /// </summary>
        Permanent
    }
}
