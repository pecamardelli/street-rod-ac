using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Opponents
{
    /// <summary>
    /// Service for evolving opponent traits based on race outcomes (event-driven)
    /// </summary>
    public interface IOpponentEvolutionService
    {
        /// <summary>
        /// Apply evolution to opponent after a race victory
        /// </summary>
        /// <param name="opponent">Opponent to evolve</param>
        /// <param name="isDominantWin">Whether the win was dominant (large margin)</param>
        void ApplyWinEvolution(Opponent opponent, bool isDominantWin = false);

        /// <summary>
        /// Apply evolution to opponent after a race loss
        /// </summary>
        /// <param name="opponent">Opponent to evolve</param>
        /// <param name="isBadlyBeaten">Whether the loss was by a large margin</param>
        void ApplyLossEvolution(Opponent opponent, bool isBadlyBeaten = false);

        /// <summary>
        /// Apply evolution to opponent after a crash or DNF
        /// </summary>
        /// <param name="opponent">Opponent to evolve</param>
        void ApplyCrashEvolution(Opponent opponent);

        /// <summary>
        /// Apply evolution to opponent after a close race (regardless of outcome)
        /// </summary>
        /// <param name="opponent">Opponent to evolve</param>
        void ApplyCloseRaceEvolution(Opponent opponent);
    }
}
