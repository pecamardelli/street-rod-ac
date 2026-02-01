using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Models.Career.Victory
{
    /// <summary>
    /// Interface for victory conditions that define how to win the game.
    /// Multiple victory types provide different paths to completion.
    /// </summary>
    public interface IVictoryCondition
    {
        /// <summary>
        /// Unique type identifier for this victory condition
        /// </summary>
        string VictoryType { get; }

        /// <summary>
        /// Display name for this victory type
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Full description of what this victory entails
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Check if this victory type is currently unlocked and available to pursue
        /// </summary>
        bool IsUnlocked(CareerState career);

        /// <summary>
        /// Get the current progress toward this victory
        /// </summary>
        VictoryProgress GetProgress(CareerState career);

        /// <summary>
        /// Check if this victory condition has been fully achieved
        /// </summary>
        bool IsAchieved(CareerState career);
    }
}
