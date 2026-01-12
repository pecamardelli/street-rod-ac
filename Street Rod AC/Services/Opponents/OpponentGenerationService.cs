using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Opponents
{
    /// <summary>
    /// Service for generating new opponents with realistic traits based on age and gender
    /// </summary>
    public class OpponentGenerationService : IOpponentGenerationService
    {
        private readonly Random _random;

        public OpponentGenerationService()
        {
            _random = new Random();
        }

        /// <summary>
        /// Generate a new opponent with random traits influenced by age and gender
        /// </summary>
        public Opponent GenerateOpponent(string name, int age, Gender gender)
        {
            // Generate base skill between 80-100
            var baseSkill = _random.Next(80, 101);

            return GenerateOpponent(name, age, gender, baseSkill);
        }

        /// <summary>
        /// Generate a new opponent with specified base skill
        /// </summary>
        public Opponent GenerateOpponent(string name, int age, Gender gender, int baseSkill)
        {
            // Ensure skill is in valid range
            var skill = Math.Max(80, Math.Min(100, baseSkill));

            // Generate base aggression (30-70 for neutral range)
            var baseAggression = _random.Next(30, 71);

            // Apply age modifier
            var ageModifier = CalculateAgeModifier(age);

            // Apply gender tendency (subtle variation, not a hard rule)
            var genderModifier = CalculateGenderModifier(gender);

            // Calculate final aggression with modifiers
            var aggression = baseAggression + ageModifier + genderModifier;

            // Clamp to valid range (0-100)
            aggression = Math.Max(0, Math.Min(100, aggression));

            // Create opponent
            var opponent = new Opponent(name, age, gender, skill, aggression);

            return opponent;
        }

        /// <summary>
        /// Calculate age-based aggression modifier
        /// Younger drivers (18-25): +10 to +20 aggression
        /// Middle-aged drivers (26-45): 0 to +5 aggression
        /// Older drivers (46+): -10 to -20 aggression
        /// </summary>
        private int CalculateAgeModifier(int age)
        {
            if (age < 26)
            {
                // Young hotshots: more aggressive
                return _random.Next(10, 21);
            }
            else if (age < 46)
            {
                // Middle-aged: neutral with slight variation
                return _random.Next(0, 6);
            }
            else
            {
                // Older veterans: more cautious
                return _random.Next(-20, -9);
            }
        }

        /// <summary>
        /// Calculate gender-based aggression modifier (subtle tendency)
        /// This is a statistical tendency, not a hard rule - allows for individual variation
        /// Male: slight positive tendency
        /// Female: slight negative tendency
        /// Other: neutral
        /// </summary>
        private int CalculateGenderModifier(Gender gender)
        {
            return gender switch
            {
                Gender.Male => _random.Next(-2, 6),      // Slight positive tendency (average +2)
                Gender.Female => _random.Next(-5, 3),    // Slight negative tendency (average -1)
                Gender.Other => _random.Next(-3, 4),     // Neutral
                _ => 0
            };
        }
    }
}
