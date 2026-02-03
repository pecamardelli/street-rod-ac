namespace Street_Rod_AC.Services.Configuration.Models
{
    /// <summary>
    /// Result of a launch operation
    /// </summary>
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
