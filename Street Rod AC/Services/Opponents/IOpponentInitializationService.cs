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
        /// Puts the King in the game when he isn't in it yet (a save from before him): out of sight, with his money
        /// and his car. Nothing when there is no King among the definitions.
        /// </summary>
        void EnsureKing(GameState gameState);

        /// <summary>
        /// Racers defined since the save began (a bigger pool in a newer version) join it, not on the street yet, each
        /// with a car and money; racers who have a portrait in their definition but none on the save get it. Once per
        /// game state.
        /// </summary>
        void EnsureNewcomers(GameState gameState);

        /// <summary>What a racer starts out with in the bank; a racer coming back to town brings as much</summary>
        decimal StartingMoney(Opponent opponent);
    }
}
