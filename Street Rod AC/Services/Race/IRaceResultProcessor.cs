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
        /// Process a validated race result and update game state
        /// </summary>
        /// <param name="result">Validated race result from Lua app</param>
        /// <param name="context">Race context from launch intent (optional)</param>
        Task ProcessRaceResultAsync(RaceResultJson result, RaceContext? context);
    }
}
