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
        /// Stops the running launch. Before AC has started it is called off (<see cref="RaceOutcome.Cancelled"/>:
        /// AC never starts, nothing is at stake). Once AC runs, the process tree (and a relaunched acs) is killed and
        /// the launch ends as after any exit (a race stopped before the finish has no result:
        /// <see cref="RaceOutcome.NoResult"/>, a wager is forfeited); the install is put back in its finally. Does
        /// nothing when no launch is under way (a wait for an earlier game to exit is not a launch).
        /// </summary>
        void CancelRace();

        /// <summary>
        /// Puts back what an earlier run left changed in the install once no AC process runs any more, holding the
        /// execution lock meanwhile, then raises <see cref="AssettoCorsaExited"/>. Returns how many cars and files
        /// went back. Gives up (0, the restore left for the next start) when the processes cannot be listed for a
        /// minute in a row.
        /// </summary>
        Task<int> RestoreWhenAcExitsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Whether an execution is currently locked (AC is running or being prepared, or the install is waiting for
        /// a game that outlived its launch to exit before it goes back): no launch can start
        /// </summary>
        bool IsExecutionLocked { get; }

        /// <summary>
        /// Whether a launch pipeline is under way (being prepared, running, or reading its results). False while the
        /// lock is only held by a wait for AC to exit.
        /// </summary>
        bool IsLaunchInProgress { get; }

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

        /// <summary>
        /// Raised when a wait for Assetto Corsa to exit (<see cref="RestoreWhenAcExitsAsync"/>, or the hand-over when
        /// a game outlived its launch) is over and the install is back, with the lock released. A race still pending
        /// can then be settled.
        /// </summary>
        event EventHandler? AssettoCorsaExited;
    }
}
