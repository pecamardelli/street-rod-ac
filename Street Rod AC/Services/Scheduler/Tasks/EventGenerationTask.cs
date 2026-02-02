using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Career.Milestones;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Services.Career;

namespace Street_Rod_AC.Services.Scheduler.Tasks
{
    /// <summary>
    /// Generates race events daily and cleans up expired ones
    /// </summary>
    public class EventGenerationTask : IScheduledTask
    {
        private readonly IRaceEventService _eventService;
        private readonly IAppLogger _logger;

        public string TaskId => "event_generation";
        public int IntervalDays => 1;

        public EventGenerationTask(IRaceEventService eventService)
        {
            _eventService = eventService;
            _logger = AppLoggerFactory.CreateLogger("EventGenerationTask");
        }

        public Task ExecuteAsync(GameState gameState, DateTime currentDate)
        {
            _logger.Information("Running daily event generation for date {Date}", currentDate);

            // Cleanup expired events
            var removed = _eventService.CleanupExpiredEvents(
                gameState.Career,
                currentDate
            );

            if (removed > 0)
            {
                _logger.Information("Removed {Count} expired events", removed);
            }

            // Generate new events
            var newEvents = _eventService.GenerateEvents(
                gameState.Career,
                currentDate
            );

            _logger.Information(
                "Event generation complete. New events: {NewCount}, Active events: {ActiveCount}",
                newEvents.Count,
                gameState.Career.ActiveEvents.Count(e => e.IsAvailable(currentDate)));

            // Update days played counter for milestones
            gameState.Career.IncrementCounter(MilestoneTrigger.DaysPlayed);

            return Task.CompletedTask;
        }
    }
}
