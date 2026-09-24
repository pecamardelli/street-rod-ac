using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Opponents
{
    /// <summary>
    /// Adapter layer that translates Street Rod opponent traits into Assetto Corsa AI parameters.
    /// This layer is:
    /// - Generated at runtime
    /// - Not persisted
    /// - Fully disposable
    /// - Tunable independently from opponent data
    ///
    /// CRITICAL: This is the ONLY place where AC AI parameters are generated.
    /// Opponent models must NEVER contain AC-specific values.
    /// </summary>
    public static class OpponentAIAdapter
    {
        /// <summary>
        /// Convert opponent traits to Assetto Corsa AI parameters
        /// </summary>
        /// <param name="opponent">The opponent to convert</param>
        /// <returns>AC AI parameters ready for race.ini configuration</returns>
        public static AssettoCorsaAIParameters ToAssettoCorsaAI(Opponent opponent)
        {
            // Direct mapping (1:1) - no transformation needed since ranges align
            // Clamped all the same: a save edited by hand or an older one may hold a skill out of range, and
            // AC takes whatever race.ini says
            var aiStrength = Math.Clamp(opponent.Skill, Opponent.MinSkill, Opponent.MaxSkill);  // 90-100
            var aiAggression = Math.Clamp(opponent.Aggression, 0, 100);

            return new AssettoCorsaAIParameters
            {
                AILevel = aiStrength,
                AIAggression = aiAggression,
                DriverName = opponent.Name
            };
        }

        /// <summary>
        /// Validate opponent traits are within acceptable ranges
        /// </summary>
        /// <param name="opponent">Opponent to validate</param>
        /// <returns>True if valid, false otherwise</returns>
        public static bool ValidateOpponent(Opponent opponent)
        {
            if (opponent.Skill < Opponent.MinSkill || opponent.Skill > Opponent.MaxSkill)
                return false;

            if (opponent.Aggression < 0 || opponent.Aggression > 100)
                return false;

            return true;
        }
    }

    /// <summary>
    /// Assetto Corsa AI parameters (engine-specific, ephemeral)
    /// These values are NEVER persisted to save files
    /// </summary>
    public class AssettoCorsaAIParameters
    {
        /// <summary>
        /// AC AI Level (skill) - Range: 90-100
        /// </summary>
        public int AILevel { get; set; }

        /// <summary>
        /// AC AI Aggression - Range: 0-100
        /// </summary>
        public int AIAggression { get; set; }

        /// <summary>
        /// Driver name to display in AC
        /// </summary>
        public string DriverName { get; set; } = string.Empty;
    }
}
