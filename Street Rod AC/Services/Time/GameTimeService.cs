using System.Globalization;
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

        /// <summary>The game speaks US English, whatever the machine's culture: "8:00 AM", "June 3, 1971"</summary>
        private static readonly CultureInfo DisplayCulture = CultureInfo.GetCultureInfo("en-US");

        /// <summary>
        /// One spend at a time. The clock moves first and the day's tasks run after it with awaits in between
        /// (the market's engines are put together on a worker thread), so a second spend started meanwhile -
        /// a quick second click, a screen spending as it closes - would run the scheduler twice over the same
        /// lists. A second spend waits for the first and then adds its time on top.
        /// </summary>
        private readonly SemaphoreSlim _advancing = new(1, 1);

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
            await _advancing.WaitAsync();
            try
            {
                return await SpendTimeCoreAsync(gameState, minutes);
            }
            finally
            {
                _advancing.Release();
            }
        }

        private async Task<TimeSpendResult> SpendTimeCoreAsync(GameState gameState, int minutes)
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
                previousTime.ToString("h:mm tt", DisplayCulture),
                newTime.ToString("h:mm tt", DisplayCulture));

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
            await _advancing.WaitAsync();
            try
            {
                var previousTime = gameState.Date;
                var remainingMinutes = GetRemainingMinutesToday(gameState);

                _logger.Information("Ending day early with {Remaining} minutes remaining", remainingMinutes);

                return await AdvanceToNextMorningAsync(gameState, previousTime, previousTime, remainingMinutes);
            }
            finally
            {
                _advancing.Release();
            }
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
            return gameState.Date.ToString("h:mm tt", DisplayCulture);
        }

        public string GetFormattedDate(GameState gameState)
        {
            return gameState.Date.ToString("MMMM d, yyyy", DisplayCulture);
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
                previousTime.ToString("MMM d h:mm tt", DisplayCulture),
                nextMorning.ToString("MMM d h:mm tt", DisplayCulture),
                daysPassed);

            // Update game state
            gameState.UpdateTime(nextMorning);

            // Run scheduled tasks for each day that passed
            var schedulerDate = previousTime.Date;
            var failed = new List<string>();
            for (int i = 0; i < daysPassed; i++)
            {
                schedulerDate = schedulerDate.AddDays(1);
                failed.AddRange(await _scheduler.OnTimeAdvancedAsync(gameState, schedulerDate.AddDays(-1), schedulerDate));
            }

            if (failed.Count > 0)
            {
                _logger.Warning("Scheduled task(s) failed while the day turned over: {Tasks}", string.Join(", ", failed.Distinct()));
            }

            return new TimeSpendResult
            {
                NewDayStarted = true,
                DaysPassed = daysPassed,
                PreviousTime = previousTime,
                NewTime = nextMorning,
                MinutesSpent = minutesSpent,
                FailedTaskIds = [.. failed.Distinct()]
            };
        }
    }
}
