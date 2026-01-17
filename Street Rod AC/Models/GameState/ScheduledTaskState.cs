namespace Street_Rod_AC.Models.GameState
{
    /// <summary>
    /// Tracks the execution state of a scheduled task
    /// </summary>
    public class ScheduledTaskState
    {
        /// <summary>
        /// Unique identifier matching IScheduledTask.TaskId
        /// </summary>
        public string TaskId { get; set; } = string.Empty;

        /// <summary>
        /// When this task was last executed (game time)
        /// </summary>
        public DateTime LastExecuted { get; set; }
    }
}
