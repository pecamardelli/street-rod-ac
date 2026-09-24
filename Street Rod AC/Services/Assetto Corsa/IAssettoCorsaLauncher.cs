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
        /// Launch a race session (any type) with the specified intent
        /// </summary>
        Task<LaunchResult> LaunchRaceAsync(LaunchIntent intent);

        /// <summary>
        /// Stops the running launch: kills the AC process tree (and a relaunched acs). The launch then ends as after
        /// any exit (a race stopped before the finish has no result: <see cref="RaceOutcome.NoResult"/>, a wager is
        /// forfeited) and the install is put back in its finally. Does nothing when nothing runs.
        /// </summary>
        void CancelRace();

        /// <summary>
        /// Puts back what an earlier run left changed in the install once no AC process runs any more, holding the
        /// execution lock meanwhile. Returns how many cars and files went back.
        /// </summary>
        Task<int> RestoreWhenAcExitsAsync(CancellationToken cancellationToken = default);

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

        /// <summary>
        /// Event fired when a race completes and changed the game state (a result applied, or a forfeit), after the
        /// install is put back. Raised outside the pipeline: a handler that throws is logged and changes nothing.
        /// </summary>
        event EventHandler? RaceCompleted;
    }
}
