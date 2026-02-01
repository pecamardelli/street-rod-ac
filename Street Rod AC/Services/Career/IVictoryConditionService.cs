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
        /// Check if any victory condition has been achieved
        /// </summary>
        IVictoryCondition? CheckForVictory(GameState gameState);

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
