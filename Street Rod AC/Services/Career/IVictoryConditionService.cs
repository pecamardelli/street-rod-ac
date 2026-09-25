using Street_Rod_AC.Models.Career.Victory;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Career
{
    /// <summary>
    /// Service for managing victory conditions and tracking progress
    /// </summary>
    public interface IVictoryConditionService
    {
        /// <summary>
        /// Get all available victory conditions
        /// </summary>
        IEnumerable<IVictoryCondition> GetAllVictoryConditions();

        /// <summary>
        /// Get victory conditions that are currently unlocked for pursuit
        /// </summary>
        IEnumerable<IVictoryCondition> GetUnlockedVictoryConditions(CareerState career);

        /// <summary>
        /// Get a specific victory condition by type
        /// </summary>
        IVictoryCondition? GetVictoryCondition(string victoryType);

        /// <summary>
        /// The victory the game has reached: the path the player picked (<see cref="CareerState.ActiveVictoryType"/>)
        /// once it is achieved, or with none picked the first one achieved. Null when none is.
        /// </summary>
        IVictoryCondition? CheckForVictory(GameState gameState);

        /// <summary>
        /// Marks the game won when <see cref="CheckForVictory"/> finds a victory and it was not won already, and
        /// returns that victory; null otherwise. A won game goes on: the player can keep racing.
        /// </summary>
        IVictoryCondition? ClaimVictory(GameState gameState);

        /// <summary>
        /// Get progress for a specific victory condition
        /// </summary>
        VictoryProgress GetProgress(string victoryType, CareerState career);

        /// <summary>
        /// Get progress for all victory conditions
        /// </summary>
        Dictionary<string, VictoryProgress> GetAllProgress(CareerState career);

        /// <summary>
        /// Check for newly unlocked victory conditions and update career state
        /// </summary>
        List<IVictoryCondition> CheckForNewUnlocks(CareerState career);
    }
}
