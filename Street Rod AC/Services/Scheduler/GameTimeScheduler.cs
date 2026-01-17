using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Scheduler
{
    /// <summary>
    /// Scheduler that executes tasks based on game time progression
    /// </summary>
    public class GameTimeScheduler : IGameTimeScheduler
    {
        private readonly List<IScheduledTask> _tasks = [];
        private readonly IAppLogger _logger;

        public GameTimeScheduler()
        {
            _logger = AppLoggerFactory.CreateLogger("Scheduler");
        }

        public void RegisterTask(IScheduledTask task)
        {
            _tasks.Add(task);
            _logger.Information("Registered scheduled task: {TaskId} (every {Interval} day(s))",
                task.TaskId, task.IntervalDays);
        }

        public async Task OnTimeAdvancedAsync(GameState gameState, DateTime previousDate, DateTime newDate)
        {
            _logger.Debug("Time advanced from {PreviousDate} to {NewDate}", previousDate, newDate);

            foreach (var task in _tasks)
            {
                if (IsTaskDue(gameState, task, newDate))
                {
                    await ExecuteTaskAsync(gameState, task, newDate);
                }
            }
        }

        public async Task ExecuteAllTasksAsync(GameState gameState, DateTime currentDate)
        {
            _logger.Information("Force executing all {Count} scheduled tasks", _tasks.Count);

            foreach (var task in _tasks)
            {
                await ExecuteTaskAsync(gameState, task, currentDate);
            }
        }

        private bool IsTaskDue(GameState gameState, IScheduledTask task, DateTime currentDate)
        {
            var taskState = GetOrCreateTaskState(gameState, task.TaskId);
            var daysSinceLastRun = (currentDate.Date - taskState.LastExecuted.Date).Days;

            return daysSinceLastRun >= task.IntervalDays;
        }

        private async Task ExecuteTaskAsync(GameState gameState, IScheduledTask task, DateTime currentDate)
        {
            _logger.Information("Executing scheduled task: {TaskId}", task.TaskId);

            try
            {
                await task.ExecuteAsync(gameState, currentDate);

                // Update last execution time
                var taskState = GetOrCreateTaskState(gameState, task.TaskId);
                taskState.LastExecuted = currentDate;

                _logger.Information("Scheduled task completed: {TaskId}", task.TaskId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Scheduled task failed: {TaskId}", task.TaskId);
            }
        }

        private ScheduledTaskState GetOrCreateTaskState(GameState gameState, string taskId)
        {
            var existing = gameState.ScheduledTasks.FirstOrDefault(t => t.TaskId == taskId);
            if (existing != null)
            {
                return existing;
            }

            var newState = new ScheduledTaskState
            {
                TaskId = taskId,
                LastExecuted = DateTime.MinValue
            };
            gameState.ScheduledTasks.Add(newState);
            return newState;
        }
    }
}
