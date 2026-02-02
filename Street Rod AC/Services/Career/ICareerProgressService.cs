using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Career
{
    /// <summary>
    /// Service for checking career progress after races and showing notifications
    /// for newly completed milestones, unlocked victories, and game victories.
    /// </summary>
    public interface ICareerProgressService
    {
        /// <summary>
        /// Check for career progress after a race. Shows notifications for:
        /// - Newly completed milestones
        /// - Newly unlocked victories
        /// - Victory achieved (game won)
        /// </summary>
        /// <param name="gameState">The current game state</param>
        /// <returns>Result containing what was unlocked/achieved</returns>
        CareerProgressResult CheckProgressAfterRace(GameState gameState);
    }

    /// <summary>
    /// Result of a career progress check
    /// </summary>
    public class CareerProgressResult
    {
        /// <summary>
        /// Milestones that were completed during this check
        /// </summary>
        public List<string> CompletedMilestones { get; set; } = [];

        /// <summary>
        /// Victory types that were unlocked during this check
        /// </summary>
        public List<string> UnlockedVictories { get; set; } = [];

        /// <summary>
        /// The victory type that was achieved (if game was won)
        /// </summary>
        public string? AchievedVictory { get; set; }

        /// <summary>
        /// Whether the player won the game
        /// </summary>
        public bool GameWon => AchievedVictory != null;

        /// <summary>
        /// Whether any progress was made (milestones, unlocks, or victory)
        /// </summary>
        public bool HasProgress => CompletedMilestones.Count > 0
            || UnlockedVictories.Count > 0
            || GameWon;
    }
}
