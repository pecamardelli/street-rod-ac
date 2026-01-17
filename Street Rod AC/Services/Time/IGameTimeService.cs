using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Time
{
    /// <summary>
    /// Manages game time progression
    /// </summary>
    public interface IGameTimeService
    {
        /// <summary>
        /// Hour when the game day starts (player wakes up)
        /// </summary>
        int DayStartHour { get; }

        /// <summary>
        /// Hour when the game day ends (player goes home)
        /// </summary>
        int DayEndHour { get; }

        /// <summary>
        /// Spends time for an action. Returns true if a new day started.
        /// </summary>
        /// <param name="gameState">Current game state</param>
        /// <param name="action">The action being performed</param>
        /// <returns>Result containing whether day changed and time spent</returns>
        Task<TimeSpendResult> SpendTimeAsync(GameState gameState, GameAction action);

        /// <summary>
        /// Spends a custom amount of time. Returns true if a new day started.
        /// </summary>
        /// <param name="gameState">Current game state</param>
        /// <param name="minutes">Minutes to spend</param>
        /// <returns>Result containing whether day changed and time spent</returns>
        Task<TimeSpendResult> SpendTimeAsync(GameState gameState, int minutes);

        /// <summary>
        /// Gets the remaining hours in the current game day
        /// </summary>
        double GetRemainingHoursToday(GameState gameState);

        /// <summary>
        /// Gets the remaining minutes in the current game day
        /// </summary>
        int GetRemainingMinutesToday(GameState gameState);

        /// <summary>
        /// Checks if there's enough time remaining today for an action
        /// </summary>
        bool HasTimeFor(GameState gameState, GameAction action);

        /// <summary>
        /// Ends the current day and advances to the next morning
        /// </summary>
        Task<TimeSpendResult> EndDayAsync(GameState gameState);

        /// <summary>
        /// Gets the current time formatted as a string (e.g., "2:30 PM")
        /// </summary>
        string GetFormattedTime(GameState gameState);

        /// <summary>
        /// Gets the current date formatted as a string (e.g., "June 15, 1963")
        /// </summary>
        string GetFormattedDate(GameState gameState);
    }

    /// <summary>
    /// Result of spending time
    /// </summary>
    public class TimeSpendResult
    {
        /// <summary>
        /// Whether a new day started as a result of spending time
        /// </summary>
        public bool NewDayStarted { get; init; }

        /// <summary>
        /// How many days passed (0 if same day, 1+ if day changed)
        /// </summary>
        public int DaysPassed { get; init; }

        /// <summary>
        /// The previous date/time before spending
        /// </summary>
        public DateTime PreviousTime { get; init; }

        /// <summary>
        /// The new date/time after spending
        /// </summary>
        public DateTime NewTime { get; init; }

        /// <summary>
        /// Minutes that were spent
        /// </summary>
        public int MinutesSpent { get; init; }
    }
}
