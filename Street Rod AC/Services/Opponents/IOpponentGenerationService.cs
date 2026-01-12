using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Opponents
{
    /// <summary>
    /// Service for generating new opponents with realistic traits
    /// </summary>
    public interface IOpponentGenerationService
    {
        /// <summary>
        /// Generate a new opponent with random traits influenced by age and gender
        /// </summary>
        /// <param name="name">Opponent name</param>
        /// <param name="age">Age (affects aggression tendencies)</param>
        /// <param name="gender">Gender (slight tendency in aggression variation)</param>
        /// <returns>New opponent with generated traits</returns>
        Opponent GenerateOpponent(string name, int age, Gender gender);

        /// <summary>
        /// Generate a new opponent with specified base skill
        /// </summary>
        /// <param name="name">Opponent name</param>
        /// <param name="age">Age (affects aggression tendencies)</param>
        /// <param name="gender">Gender (slight tendency in aggression variation)</param>
        /// <param name="baseSkill">Base skill level (80-100)</param>
        /// <returns>New opponent with generated traits</returns>
        Opponent GenerateOpponent(string name, int age, Gender gender, int baseSkill);
    }
}
