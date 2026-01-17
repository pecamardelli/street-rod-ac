using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Scheduler
{
    /// <summary>
    /// Scheduler that executes tasks based on game time progression
    /// </summary>
    public interface IGameTimeScheduler
    {
        /// <summary>
        /// Register a task to be executed on schedule
        /// </summary>
        void RegisterTask(IScheduledTask task);

        /// <summary>
        /// Called when game time advances. Executes any due tasks.
        /// </summary>
        /// <param name="gameState">Current game state</param>
        /// <param name="previousDate">Date before advancement</param>
        /// <param name="newDate">Date after advancement</param>
        Task OnTimeAdvancedAsync(GameState gameState, DateTime previousDate, DateTime newDate);

        /// <summary>
        /// Force execution of all tasks (useful for new game initialization)
        /// </summary>
        Task ExecuteAllTasksAsync(GameState gameState, DateTime currentDate);
    }
}
