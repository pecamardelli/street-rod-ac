using Street_Rod_AC.Models.Career.Milestones;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Career
{
    /// <summary>
    /// Service for managing milestone definitions and tracking progress
    /// </summary>
    public interface IMilestoneService
    {
        /// <summary>
        /// Get all milestone definitions
        /// </summary>
        IEnumerable<MilestoneDefinition> GetAllMilestones();

        /// <summary>
        /// Get a specific milestone by ID
        /// </summary>
        MilestoneDefinition? GetMilestone(string milestoneId);

        /// <summary>
        /// Get milestones by category
        /// </summary>
        IEnumerable<MilestoneDefinition> GetMilestonesByCategory(string category);

        /// <summary>
        /// Get progress for a specific milestone
        /// </summary>
        MilestoneProgress GetProgress(string milestoneId, CareerState career);

        /// <summary>
        /// Get progress for all milestones
        /// </summary>
        IEnumerable<MilestoneProgress> GetAllProgress(CareerState career);

        /// <summary>
        /// Check for newly completed milestones and return them
        /// Updates the CareerState.CompletedMilestones set
        /// </summary>
        List<MilestoneDefinition> CheckForCompletedMilestones(CareerState career);

        /// <summary>
        /// Get unlocks for a completed milestone
        /// </summary>
        List<string> GetUnlocksForMilestone(string milestoneId);

        /// <summary>
        /// Check if a specific milestone is completed
        /// </summary>
        bool IsMilestoneCompleted(string milestoneId, CareerState career);
    }
}
