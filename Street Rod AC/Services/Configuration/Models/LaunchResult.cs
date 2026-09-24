namespace Street_Rod_AC.Services.Configuration.Models
{
    /// <summary>
    /// Result of a launch operation
    /// </summary>
    /// <summary>What came of a race launch, for the screen that tells the player</summary>
    public enum RaceOutcome
    {
        /// <summary>A showroom or a free run: nothing to ingest</summary>
        NotARace,

        /// <summary>The result was read and applied</summary>
        Processed,

        /// <summary>AC closed without a result (the player quit or stopped the race); a wager race is forfeited</summary>
        NoResult,

        /// <summary>A result was written but could not be used; it is kept in quarantine and nothing was applied</summary>
        Quarantined,

        /// <summary>The launch or the ingestion failed (AC did not start, closed at once, or the result could not be read)</summary>
        Failed
    }

    public class LaunchResult
    {
        /// <summary>
        /// Whether the launch was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if launch failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// When the launch operation started
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// When the launch operation ended
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// Total duration
        /// </summary>
        public TimeSpan Duration => EndTime - StartTime;

        /// <summary>
        /// Exit code from Assetto Corsa process (if available)
        /// </summary>
        public int? ExitCode { get; set; }

        /// <summary>
        /// Any additional diagnostic information
        /// </summary>
        public Dictionary<string, object> Diagnostics { get; set; } = new();

        /// <summary>What came of the race; <see cref="RaceOutcome.NotARace"/> for a showroom or a free run</summary>
        public RaceOutcome Outcome { get; set; }

        /// <summary>What the player should be told about the race, one dialog each, in order</summary>
        public List<Street_Rod_AC.Models.Race.PlayerMessage> PlayerMessages { get; } = new();

        /// <summary>
        /// Create a successful result
        /// </summary>
        public static LaunchResult CreateSuccess(DateTime startTime, DateTime endTime, int? exitCode = null)
        {
            return new LaunchResult
            {
                Success = true,
                StartTime = startTime,
                EndTime = endTime,
                ExitCode = exitCode
            };
        }

        /// <summary>
        /// Create a failed result
        /// </summary>
        public static LaunchResult CreateFailure(string errorMessage, DateTime startTime, DateTime endTime)
        {
            return new LaunchResult
            {
                Success = false,
                ErrorMessage = errorMessage,
                StartTime = startTime,
                EndTime = endTime
            };
        }
    }
}
