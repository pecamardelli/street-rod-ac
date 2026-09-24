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
        /// The id of the definition this opponent was made from ("drv_001" in opponent_definitions.json); empty
        /// for one that was generated. <see cref="OpponentId"/> is made from it, so it is stable across loads.
        /// </summary>
        public string DefinitionId { get; set; } = string.Empty;

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
        /// Driving skill level (90-100)
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

        /// <summary>Lowest skill an opponent drives with. 90 on purpose: below that AC's AI is too slow to be a race</summary>
        public const int MinSkill = 90;

        /// <summary>Highest skill: AC's AI_LEVEL tops out at 100</summary>
        public const int MaxSkill = 100;

        /// <summary>
        /// Clamp skill to valid range (90-100)
        /// </summary>
        private static int ClampSkill(int value) => Math.Clamp(value, MinSkill, MaxSkill);

        /// <summary>
        /// Clamp aggression to valid range (0-100)
        /// </summary>
        private static int ClampAggression(int value) => Math.Clamp(value, 0, 100);
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
