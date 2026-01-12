namespace Street_Rod_AC.Models.GameState
{
    /// <summary>
    /// Represents an AI opponent racer with personality and driving traits.
    /// This is the engine-agnostic layer that defines WHO the racer is.
    /// AC-specific AI parameters are derived at runtime by the adapter layer.
    /// </summary>
    public class Opponent : Racer
    {
        /// <summary>
        /// Unique identifier for this opponent
        /// </summary>
        public Guid OpponentId { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Opponent nickname (e.g., "Flathead", "Redline")
        /// </summary>
        public string Nickname { get; set; } = string.Empty;

        /// <summary>
        /// Age of the opponent (affects aggression tendencies)
        /// </summary>
        public int Age { get; set; }

        /// <summary>
        /// Gender of the opponent (used as tendency in aggression variation)
        /// </summary>
        public Gender Gender { get; set; }

        /// <summary>
        /// Driving skill level (80-100)
        /// Maps to AC AI Strength
        /// </summary>
        public int Skill { get; set; }

        /// <summary>
        /// Risk appetite and racing aggression (0-100)
        /// Maps to AC AI Aggression
        /// </summary>
        public int Aggression { get; set; }

        /// <summary>
        /// Optional portrait reference for UI display
        /// </summary>
        public string? PortraitPath { get; set; }

        /// <summary>
        /// Neighborhood or location flavor text
        /// </summary>
        public string? Location { get; set; }

        /// <summary>
        /// Optional background lore or personality description
        /// </summary>
        public string? Biography { get; set; }

        /// <summary>
        /// Constructor for creating a new opponent
        /// </summary>
        public Opponent(string name, int age, Gender gender, int skill, int aggression)
            : base(RacerType.AI, name)
        {
            Age = age;
            Gender = gender;
            Skill = ClampSkill(skill);
            Aggression = ClampAggression(aggression);
        }

        /// <summary>
        /// Parameterless constructor for LiteDB
        /// </summary>
        public Opponent() : base(RacerType.AI, "Unknown Opponent")
        {
            Age = 25;
            Gender = Gender.Male;
            Skill = 90;
            Aggression = 50;
        }

        /// <summary>
        /// Adjust skill by a delta amount (event-driven evolution)
        /// </summary>
        public void AdjustSkill(int delta)
        {
            Skill = ClampSkill(Skill + delta);
        }

        /// <summary>
        /// Adjust aggression by a delta amount (event-driven evolution)
        /// </summary>
        public void AdjustAggression(int delta)
        {
            Aggression = ClampAggression(Aggression + delta);
        }

        /// <summary>
        /// Clamp skill to valid range (80-100)
        /// </summary>
        private static int ClampSkill(int value)
        {
            return Math.Max(80, Math.Min(100, value));
        }

        /// <summary>
        /// Clamp aggression to valid range (0-100)
        /// </summary>
        private static int ClampAggression(int value)
        {
            return Math.Max(0, Math.Min(100, value));
        }
    }

    /// <summary>
    /// Gender enum (used for tendency in personality generation)
    /// </summary>
    public enum Gender
    {
        Male,
        Female,
        Other
    }
}
