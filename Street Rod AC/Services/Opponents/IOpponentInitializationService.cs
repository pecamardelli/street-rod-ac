using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Opponents
{
    /// <summary>
    /// Service for initializing opponents when creating a new game
    /// </summary>
    public interface IOpponentInitializationService
    {
        /// <summary>
        /// Initialize opponents for a new game
        /// Loads opponent definitions and adds them to the game state
        /// </summary>
        /// <param name="gameState">The game state to initialize</param>
        /// <param name="opponentCount">Number of opponents to initialize (default: all available)</param>
        void InitializeOpponents(GameState gameState, int? opponentCount = null);

        /// <summary>
        /// Get random selection of opponents
        /// </summary>
        /// <param name="count">Number of opponents to select</param>
        /// <returns>List of randomly selected opponents</returns>
        List<Opponent> GetRandomOpponents(int count);
    }
}
