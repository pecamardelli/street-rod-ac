using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Scheduler
{
    /// <summary>
    /// Represents a task that runs on a game time schedule
    /// </summary>
    public interface IScheduledTask
    {
        /// <summary>
        /// Unique identifier for this task (used for tracking execution state)
        /// </summary>
        string TaskId { get; }

        /// <summary>
        /// How often this task should run in game days
        /// </summary>
        int IntervalDays { get; }

        /// <summary>
        /// Execute the scheduled task
        /// </summary>
        /// <param name="gameState">Current game state to modify</param>
        /// <param name="currentDate">Current game date</param>
        Task ExecuteAsync(GameState gameState, DateTime currentDate);
    }
}
