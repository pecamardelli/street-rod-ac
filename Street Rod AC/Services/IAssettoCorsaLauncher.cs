using Street_Rod_AC.Services.Configuration.Models;

namespace Street_Rod_AC.Services
{
    /// <summary>
    /// Centralized service for launching Assetto Corsa.
    /// Implements the complete execution pipeline: Declare → Prepare → Lock → Execute → Monitor → Cleanup
    ///
    /// This is the ONLY system that may:
    /// - Launch Assetto Corsa executables
    /// - Prepare configuration for execution
    /// - Track execution lifecycle
    ///
    /// All launches follow the transaction model:
    /// 1. Declare intent (high-level)
    /// 2. Prepare configuration (apply intents)
    /// 3. Lock execution state
    /// 4. Execute Assetto Corsa
    /// 5. Monitor passively
    /// 6. Cleanup and restore
    /// </summary>
    public interface IAssettoCorsaLauncher
    {
        /// <summary>
        /// Launch Assetto Corsa showroom with the specified intent
        /// </summary>
        Task<LaunchResult> LaunchShowroomAsync(ShowroomLaunchIntent intent);

        /// <summary>
        /// Launch a full race session with the specified intent
        /// </summary>
        Task<LaunchResult> LaunchRaceAsync(RaceLaunchIntent intent);

        /// <summary>
        /// Whether an execution is currently locked (AC is running or being prepared)
        /// </summary>
        bool IsExecutionLocked { get; }

        /// <summary>
        /// Whether Assetto Corsa is currently running
        /// </summary>
        bool IsAssettoCorsaRunning { get; }

        /// <summary>
        /// Event fired when Assetto Corsa execution starts
        /// </summary>
        event EventHandler<LaunchIntent>? ExecutionStarted;

        /// <summary>
        /// Event fired when Assetto Corsa execution ends
        /// </summary>
        event EventHandler<LaunchResult>? ExecutionEnded;
    }
}
