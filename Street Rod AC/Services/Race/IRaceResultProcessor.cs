using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;

namespace Street_Rod_AC.Services.Race
{
    /// <summary>
    /// Service for processing race results and applying game logic
    /// Updates racer stats, car health, money, and handles pink slips
    /// </summary>
    public interface IRaceResultProcessor
    {
        /// <summary>
        /// Process a validated race result and update game state. Everything that can fail on the file's data
        /// fails before the state is touched; the state and the session record are then saved together.
        /// Runs on the thread that owns the game state (the UI thread).
        /// </summary>
        /// <param name="result">Validated race result from Lua app</param>
        /// <param name="context">The race the result belongs to (its context id was matched by the caller)</param>
        /// <param name="gameState">The loaded game the race was run in</param>
        /// <returns>What the player should be told (event rewards, career progress)</returns>
        List<PlayerMessage> ProcessRaceResult(RaceResultJson result, RaceContext context, GameState gameState);

        /// <summary>
        /// Settles a race with stakes that brought back no result as a loss for the player, records it and
        /// saves. The caller checks that there are stakes and that the race is not settled already.
        /// </summary>
        /// <returns>What the player should be told</returns>
        List<PlayerMessage> ApplyForfeit(RaceContext context, GameState gameState);

        /// <summary>
        /// A race without stakes that brought back no result: it is no longer pending. Clears
        /// <c>GameState.PendingRace</c> if it is this race, and saves.
        /// </summary>
        void ReleasePendingRace(RaceContext context, GameState gameState);
    }
}
