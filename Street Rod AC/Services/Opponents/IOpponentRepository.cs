using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Opponents
{
    /// <summary>
    /// Repository for loading opponent definitions from storage
    /// </summary>
    public interface IOpponentRepository
    {
        /// <summary>
        /// Load all opponent definitions from the opponent definitions JSON file
        /// </summary>
        /// <returns>List of all available opponents</returns>
        List<Opponent> LoadAllOpponents();

        /// <summary>
        /// Load a specific opponent by ID
        /// </summary>
        /// <param name="opponentId">The opponent ID (e.g., "drv_001")</param>
        /// <returns>The opponent, or null if not found</returns>
        Opponent? LoadOpponent(string opponentId);

        /// <summary>
        /// Check if opponent definitions file exists
        /// </summary>
        /// <returns>True if the definitions file exists</returns>
        bool DefinitionsFileExists();
    }
}
