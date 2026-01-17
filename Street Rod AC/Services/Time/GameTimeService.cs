using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Services.Scheduler;

namespace Street_Rod_AC.Services.Time
{
    /// <summary>
    /// Manages game time progression with day boundaries and scheduler integration
    /// </summary>
    public class GameTimeService : IGameTimeService
    {
        private readonly IGameTimeScheduler _scheduler;
        private readonly IAppLogger _logger;

        public int DayStartHour => 8;  // 8:00 AM
        public int DayEndHour => 22;   // 10:00 PM

        public GameTimeService(IGameTimeScheduler scheduler)
        {
            _scheduler = scheduler;
            _logger = AppLoggerFactory.CreateLogger("GameTime");
        }

        public async Task<TimeSpendResult> SpendTimeAsync(GameState gameState, GameAction action)
        {
            var minutes = action.GetTimeInMinutes();
            _logger.Debug("Spending time for action {Action}: {Minutes} minutes", action, minutes);
            return await SpendTimeAsync(gameState, minutes);
        }

        public async Task<TimeSpendResult> SpendTimeAsync(GameState gameState, int minutes)
        {
            var previousTime = gameState.Date;
            var previousDay = previousTime.Date;

            // Add the time
            var newTime = previousTime.AddMinutes(minutes);

            // Check if we've gone past end of day
            if (newTime.Hour >= DayEndHour || newTime.Date > previousDay)
            {
                // Advance to next morning
                return await AdvanceToNextMorningAsync(gameState, previousTime, newTime, minutes);
            }

            // Same day, just update time
            gameState.UpdateTime(newTime);

            _logger.Debug("Time advanced: {Previous} -> {New}",
                previousTime.ToString("h:mm tt"),
                newTime.ToString("h:mm tt"));

            return new TimeSpendResult
            {
                NewDayStarted = false,
                DaysPassed = 0,
                PreviousTime = previousTime,
                NewTime = newTime,
                MinutesSpent = minutes
            };
        }

        public async Task<TimeSpendResult> EndDayAsync(GameState gameState)
        {
            var previousTime = gameState.Date;
            var remainingMinutes = GetRemainingMinutesToday(gameState);

            _logger.Information("Ending day early with {Remaining} minutes remaining", remainingMinutes);

            return await AdvanceToNextMorningAsync(gameState, previousTime, previousTime, remainingMinutes);
        }

        public double GetRemainingHoursToday(GameState gameState)
        {
            return GetRemainingMinutesToday(gameState) / 60.0;
        }

        public int GetRemainingMinutesToday(GameState gameState)
        {
            var current = gameState.Date;
            var endOfDay = current.Date.AddHours(DayEndHour);

            if (current >= endOfDay)
                return 0;

            return (int)(endOfDay - current).TotalMinutes;
        }

        public bool HasTimeFor(GameState gameState, GameAction action)
        {
            var requiredMinutes = action.GetTimeInMinutes();
            var remainingMinutes = GetRemainingMinutesToday(gameState);
            return remainingMinutes >= requiredMinutes;
        }

        public string GetFormattedTime(GameState gameState)
        {
            return gameState.Date.ToString("h:mm tt");
        }

        public string GetFormattedDate(GameState gameState)
        {
            return gameState.Date.ToString("MMMM d, yyyy");
        }

        private async Task<TimeSpendResult> AdvanceToNextMorningAsync(
            GameState gameState,
            DateTime previousTime,
            DateTime attemptedTime,
            int minutesSpent)
        {
            // Calculate next morning
            var nextMorning = previousTime.Date.AddDays(1).AddHours(DayStartHour);

            // If the attempted time went multiple days forward (unlikely but handle it)
            while (nextMorning <= attemptedTime)
            {
                nextMorning = nextMorning.AddDays(1);
            }

            var daysPassed = (nextMorning.Date - previousTime.Date).Days;

            _logger.Information(
                "Day ended. Advancing from {Previous} to {Next} ({Days} day(s) passed)",
                previousTime.ToString("MMM d h:mm tt"),
                nextMorning.ToString("MMM d h:mm tt"),
                daysPassed);

            // Update game state
            gameState.UpdateTime(nextMorning);

            // Run scheduled tasks for each day that passed
            var schedulerDate = previousTime.Date;
            for (int i = 0; i < daysPassed; i++)
            {
                schedulerDate = schedulerDate.AddDays(1);
                await _scheduler.OnTimeAdvancedAsync(gameState, schedulerDate.AddDays(-1), schedulerDate);
            }

            return new TimeSpendResult
            {
                NewDayStarted = true,
                DaysPassed = daysPassed,
                PreviousTime = previousTime,
                NewTime = nextMorning,
                MinutesSpent = minutesSpent
            };
        }
    }
}
