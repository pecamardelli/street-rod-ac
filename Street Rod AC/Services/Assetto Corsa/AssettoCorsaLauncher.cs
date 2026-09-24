using System.Diagnostics;
using System.IO;
using Street_Rod_AC.Configuration;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Services.Configuration;
using Street_Rod_AC.Services.Configuration.Models;

namespace Street_Rod_AC.Services
{
    /// <summary>
    /// Centralized service for launching Assetto Corsa.
    /// Implements the complete execution pipeline following the transaction model.
    /// </summary>
    public class AssettoCorsaLauncher(
        IIniModificationService iniService,
        Race.IRaceResultIngestionService raceResultService,
        CarDataOverlay carData) : IAssettoCorsaLauncher
    {
        /// <summary>
        /// A race whose game closed sooner than this never ran: AC failed to load (a missing DLL, CSP failing, a
        /// broken car). No race is that short, since loading alone takes longer.
        /// </summary>
        private static readonly TimeSpan ShortestRace = TimeSpan.FromSeconds(15);

        /// <summary>How long to watch for a relaunched game (Steam restarts acs.exe under a new process) after ours exits</summary>
        private static readonly TimeSpan RelaunchGrace = TimeSpan.FromSeconds(3);

        /// <summary>How long a stopped race's processes get to go before the wait for them gives up</summary>
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(15);

        /// <summary>How often the wait for AC to exit looks again</summary>
        private static readonly TimeSpan ExitPoll = TimeSpan.FromSeconds(2);

        /// <summary>
        /// How many looks in a row may fail to list the processes before the wait for AC to exit gives up (a minute
        /// of them): the wait holds the execution lock, and an error that keeps coming back must not keep every race
        /// from starting for the rest of the session. The install then goes back at the next start.
        /// </summary>
        private const int MaxUnknownPolls = 30;

        /// <summary>A result file that is there but locked (a virus scanner right after the write) is tried this often...</summary>
        private const int IngestionAttempts = 3;

        /// <summary>...this far apart, before the race is left pending for a later pass</summary>
        private static readonly TimeSpan IngestionRetryDelay = TimeSpan.FromSeconds(2);

        private readonly IIniModificationService _iniService = iniService;
        private readonly Race.IRaceResultIngestionService _raceResultService = raceResultService;
        private readonly CarDataOverlay _carData = carData;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("ACLauncher");
        private readonly SemaphoreSlim _executionLock = new(1, 1);

        private volatile Process? _currentProcess;
        private volatile string? _currentProcessName;
        private CancellationTokenSource? _monitorCts;
        private volatile bool _stopRequested;
        private volatile bool _isExecutionLocked;
        private volatile bool _launchInProgress;

        public bool IsExecutionLocked => _isExecutionLocked;

        public bool IsLaunchInProgress => _launchInProgress;

        public bool IsAssettoCorsaRunning
        {
            get
            {
                var process = _currentProcess;
                try
                {
                    return process != null && !process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    // Disposed between the read and the check: it is over
                    return false;
                }
            }
        }

        public event EventHandler<LaunchIntent>? ExecutionStarted;
        public event EventHandler<LaunchResult>? ExecutionEnded;
        public event EventHandler? RaceCompleted;
        public event EventHandler? AssettoCorsaExited;

        public async Task<LaunchResult> LaunchShowroomAsync(ShowroomLaunchIntent intent)
        {
            return await ExecuteLaunchPipeline(intent);
        }

        public async Task<LaunchResult> LaunchRaceAsync(LaunchIntent intent)
        {
            return await ExecuteLaunchPipeline(intent);
        }

        /// <summary>
        /// Stops the running launch. Before AC has started, the launch is called off: AC never starts, what was
        /// prepared goes back, and nothing is at stake. Once it runs: the process started and its children, and any
        /// other AC process of that name (a relaunch), are killed, and the pipeline carries on as after any exit
        /// (a race stopped before the finish has no result, so a wager race is forfeited; the install goes back in
        /// the finally). Only the launch pipeline is stopped: a wait for an earlier game to exit is left alone.
        /// </summary>
        public void CancelRace()
        {
            if (!_launchInProgress) return;

            _stopRequested = true;

            var process = _currentProcess;
            if (process == null)
            {
                // Still preparing: the pipeline sees the flag before it would start AC. No timer: there is nothing
                // to wait for, and one armed now would go off in the middle of a race that started after all.
                _logger.Warning("The launch is called off at the player's request before Assetto Corsa started");
                return;
            }

            _logger.Warning("Stopping Assetto Corsa at the player's request");
            KillStartedGame(process);
        }

        /// <summary>Kills the game the pipeline started (and a relaunch), and bounds the wait for it to go</summary>
        private void KillStartedGame(Process process)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                _logger.Warning(ex, "Could not kill the Assetto Corsa process");
            }

            AcProcesses.KillAll(_currentProcessName ?? AcProcesses.Race);

            // A process that will not die must not keep the pipeline (and the lock, and the install) forever
            try
            {
                _monitorCts?.CancelAfter(StopTimeout);
            }
            catch (ObjectDisposedException)
            {
                // The pipeline finished in between
            }
        }

        /// <summary>
        /// Puts back whatever an earlier run left changed in the install (car data, copies, cfg INI files), once no
        /// AC process runs any more. Holds the execution lock meanwhile, so no launch starts on a half-restored
        /// install. For start-up when a game (a race the app launched before it was restarted) is still running.
        /// Raises <see cref="AssettoCorsaExited"/> after it.
        /// </summary>
        public async Task<int> RestoreWhenAcExitsAsync(CancellationToken cancellationToken = default)
        {
            await _executionLock.WaitAsync(cancellationToken);
            _isExecutionLocked = true;
            return await WaitRestoreAndReleaseAsync(cancellationToken);
        }

        /// <summary>
        /// The wait for AC to exit, then the restore, with the execution lock already held; releases it at the end,
        /// however it ends. Gives up (0, the install left for the next start) when the processes cannot be listed
        /// for <see cref="MaxUnknownPolls"/> looks in a row.
        /// </summary>
        private async Task<int> WaitRestoreAndReleaseAsync(CancellationToken cancellationToken)
        {
            var restored = 0;
            try
            {
                var unknown = 0;
                while (true)
                {
                    var running = AcProcesses.QueryAnyRunning();
                    if (running == false) break;

                    if (running == null)
                    {
                        if (++unknown >= MaxUnknownPolls)
                        {
                            _logger.Error("The running processes could not be listed for {Seconds}s: the install is left for the next start to put back",
                                (int)(MaxUnknownPolls * ExitPoll.TotalSeconds));
                            return 0;
                        }
                    }
                    else
                    {
                        unknown = 0;
                    }

                    await Task.Delay(ExitPoll, cancellationToken);
                }

                restored = RestoreInstall();
            }
            finally
            {
                _isExecutionLocked = false;
                _executionLock.Release();
            }

            // Whoever waits on a race that outlived its launch (or the app) hears of it with the lock released
            RaiseSafely("AssettoCorsaExited", () => AssettoCorsaExited?.Invoke(this, EventArgs.Empty));
            return restored;
        }

        /// <summary>The finally's hand-over when AC outlived the pipeline: the wait and the restore, in the background, logged</summary>
        private async Task RestoreAfterExitAsync()
        {
            try
            {
                var restored = await WaitRestoreAndReleaseAsync(CancellationToken.None);
                _logger.Information("Assetto Corsa closed after its launch had ended: {Count} car(s) and file(s) put back", restored);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not put the install back once Assetto Corsa closed; the next start tries again");
            }
        }

        /// <summary>
        /// Execute the complete launch pipeline: Declare → Prepare → Lock → Execute → Monitor → Cleanup
        /// </summary>
        private async Task<LaunchResult> ExecuteLaunchPipeline(LaunchIntent intent)
        {
            var startTime = DateTime.Now;
            _logger.Information("=== LAUNCH PIPELINE STARTED ===");
            _logger.Information("Intent: {Description}", intent.Description);

            // PHASE 1: ACQUIRE EXECUTION LOCK
            var lockAcquired = await _executionLock.WaitAsync(0);
            if (!lockAcquired)
            {
                var error = "Cannot launch: another execution is in progress";
                _logger.Warning(error);
                var busy = Failure(intent, error, startTime);
                busy.PlayerMessages.Add(new PlayerMessage("Assetto Corsa Is Busy",
                    "Assetto Corsa is still running, or what an earlier race changed in it is still being put back. Try again once it has closed."));
                return busy;
            }

            _isExecutionLocked = true;
            _launchInProgress = true;
            _stopRequested = false;
            _monitorCts = new CancellationTokenSource();

            // What is still listed as changed from an earlier race is a leftover from here on, not this race's
            try
            {
                _carData.BeginRace();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not reset the list of changed cars");
            }

            var run = new Run(intent);
            LaunchResult result;
            var raceCompleted = false;
            var handedOver = false;

            try
            {
                (result, raceCompleted) = await RunAsync(run, startTime, _monitorCts.Token);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Launch pipeline failed with exception");
                result = Failure(intent, ex.Message, startTime);

                // AC never started: the race did not happen. Once it had, the race is settled after it exits.
                if (run.Context != null && run.Began)
                {
                    if (run.Started)
                    {
                        result.Outcome = RaceOutcome.ResultPending;
                    }
                    else
                    {
                        await ReleaseAsync(run.Context);
                    }
                }

                _logger.Information("=== LAUNCH PIPELINE FAILED ===");
            }
            finally
            {
                if (run.Context != null && run.Began)
                    _raceResultService.EndRace(run.Context);

                // Always put the install back, release lock and cleanup. Not under a running game, though: it may
                // be reading the data and holding the sound bank; then it goes back once the game has closed, and
                // the lock stays held until then, so no race starts on the previous race's data.
                var acState = AcProcesses.QueryAnyRunning();
                if (acState != false)
                {
                    _logger.Error("Assetto Corsa {State}: the cars' data and the cfg files go back once it has closed",
                        acState == true ? "is still running" : "may still be running");
                    handedOver = true;
                }
                else
                {
                    RestoreInstall();
                }

                var process = _currentProcess;
                _currentProcess = null;
                _currentProcessName = null;
                process?.Dispose();

                _monitorCts.Dispose();
                _monitorCts = null;
                _launchInProgress = false;

                if (handedOver)
                {
                    _ = RestoreAfterExitAsync();
                }
                else
                {
                    _isExecutionLocked = false;
                    _executionLock.Release();
                }
            }

            // Subscribers hear of it once everything is back and unlocked; one that throws spoils neither the result
            // nor the other subscribers
            RaiseSafely("ExecutionEnded", () => ExecutionEnded?.Invoke(this, result));
            if (raceCompleted)
            {
                _logger.Information("Raising RaceCompleted event");
                RaiseSafely("RaceCompleted", () => RaceCompleted?.Invoke(this, EventArgs.Empty));
            }

            return result;
        }

        /// <summary>What one launch knows about its race as it goes</summary>
        private sealed class Run(LaunchIntent intent)
        {
            public LaunchIntent Intent { get; } = intent;

            /// <summary>The race's context; null for a showroom, a free run, or a race without one</summary>
            public RaceContext? Context { get; } =
                intent is DragRaceLaunchIntent && intent.Metadata.TryGetValue("RaceContext", out var value) ? value as RaceContext : null;

            /// <summary>The race was taken on by the ingestion service (pending in the save, in flight)</summary>
            public bool Began { get; set; }

            /// <summary>AC was started</summary>
            public bool Started { get; set; }

            /// <summary>What the player should hear about an earlier race settled before this one started</summary>
            public List<PlayerMessage>? EarlierMessages { get; set; }
        }

        /// <summary>The pipeline between the lock and the cleanup: prepare, start, wait, read the results</summary>
        private async Task<(LaunchResult Result, bool RaceCompleted)> RunAsync(Run run, DateTime startTime, CancellationToken cancellationToken)
        {
            var intent = run.Intent;
            var context = run.Context;

            // PHASE 1.5: THE RACE GOES ON RECORD
            // An earlier race of this save that is still waiting for its result is settled first; if it cannot be
            // yet, this one does not start, so the earlier one is never lost under it
            if (context != null)
            {
                var messages = new List<PlayerMessage>();
                run.Began = await _raceResultService.BeginRaceAsync(context, messages);
                if (!run.Began)
                {
                    var blocked = Failure(intent, "The last race has not been settled yet", startTime);
                    blocked.PlayerMessages.AddRange(messages);
                    return (blocked, false);
                }

                // The earlier race's outcome (if it was settled just now) comes first
                if (messages.Count > 0)
                    _logger.Information("{Count} message(s) about the earlier race go with this one", messages.Count);
                run.EarlierMessages = messages;
            }

            // PHASE 2: PREPARE CONFIGURATION
            _logger.Information("PHASE: Prepare Configuration");
            var configIntents = intent.GetConfigurationIntents().ToList();
            _logger.Information("Applying {Count} configuration intent(s)", configIntents.Count);

            // Under the install's gate: a restore from another thread (the crash path) never falls between a kept
            // original and the write it is kept for. (Never held across an await: failures leave the block first.)
            string? prepareError = null;
            using (AcInstallGate.Hold())
            {
                foreach (var configIntent in configIntents)
                {
                    _logger.Debug("Applying: {Description}", configIntent.Description);

                    if (!_iniService.ApplyIntent(configIntent))
                    {
                        prepareError = $"Failed to apply configuration intent: {configIntent.Description}";
                        break;
                    }
                }
            }

            if (prepareError != null)
            {
                _logger.Error(prepareError);
                return (await NotStartedAsync(run, Failure(intent, prepareError, startTime)), false);
            }

            _logger.Information("Configuration prepared successfully");

            // The garage's engines go quiet and let go of their banks: the game is about to sound its own, and a bank
            // that is open cannot be moved aside for the sound a car races with
            await Audio.EngineAudio.Shared.UnloadAllAsync();

            if (_stopRequested)
                return (await NotStartedAsync(run, Cancelled(intent, startTime)), false);

            RaiseSafely("ExecutionStarted", () => ExecutionStarted?.Invoke(this, intent));

            // From the cars' data to the start of the game, one synchronous stretch under the install's gate
            using (AcInstallGate.Hold())
            {
                // PHASE 2.5: THE CARS' OWN DATA AND SOUND
                // What the parts make of each car goes into the install now and comes out in the finally,
                // whatever happens in between. A second car of a model already in the race races in a copy of the
                // folder, made first, from the originals, before the other car's changes go in.
                foreach (var car in intent.CarData.Where(c => c.CloneId != null))
                {
                    _logger.Information("PHASE: Copy of {Car} as {Clone}", car.CarId, car.CloneId);
                    _carData.CreateClone(car.CarId, car.CloneId!, car.Build.Files, car.Build.Sound);
                }

                foreach (var car in intent.CarData.Where(c => c.CloneId == null))
                {
                    _logger.Information("PHASE: Car data for {Car}", car.CarId);
                    if (!_carData.Apply(car.CarId, car.Build.Files, car.Build.Sound))
                        _logger.Warning("{Car}: its data was already changed for this race, so it races on the other car's", car.CarId);
                }

                // PHASE 3: VALIDATE EXECUTABLE
                _logger.Information("PHASE: Validate Executable");
                var exePath = GetExecutablePath(intent.Executable);

                if (!File.Exists(exePath))
                {
                    prepareError = $"Executable not found: {exePath}";
                }
                else if (!_stopRequested) // the last moment a stop can call the launch off: after this, AC runs
                {
                    _logger.Information("Executable validated: {ExePath}", exePath);

                    // PHASE 4: EXECUTE
                    _logger.Information("PHASE: Execute");
                    _logger.Information("Launching: {Executable}", intent.Executable);

                    var processStartInfo = new ProcessStartInfo
                    {
                        FileName = exePath,
                        WorkingDirectory = AppSettings.Instance.AssettoCorsaPath,
                        UseShellExecute = true
                    };

                    _currentProcessName = AcProcesses.NameOf(intent.Executable);
                    _currentProcess = Process.Start(processStartInfo);
                }
            }

            if (prepareError != null)
            {
                _logger.Error(prepareError);
                return (await NotStartedAsync(run, Failure(intent, prepareError, startTime)), false);
            }

            if (_currentProcess == null && _stopRequested)
                return (await NotStartedAsync(run, Cancelled(intent, startTime)), false);

            var started = _currentProcess;
            if (started == null)
            {
                var error = "Failed to start process";
                _logger.Error(error);
                return (await NotStartedAsync(run, Failure(intent, error, startTime)), false);
            }

            run.Started = true;
            var processStarted = DateTime.Now;
            _logger.Information("Process started. PID: {ProcessId}", started.Id);
            if (context != null) _raceResultService.MarkRaceLaunched(context);

            // A stop that came in (from another thread) between the last check and the start
            if (_stopRequested)
            {
                _logger.Warning("A stop came in as Assetto Corsa started: stopping it");
                KillStartedGame(started);
            }

            // PHASE 5: MONITOR
            // For races, the Lua race manager will call ac.shutdownAssettoCorsa() when done
            _logger.Information("PHASE: Monitor");
            _logger.Information("Waiting for Assetto Corsa to exit...");

            int exitCode;
            DateTime lastExit;
            try
            {
                await started.WaitForExitAsync(cancellationToken);
                exitCode = started.ExitCode;

                // The process we started is not always the game: Steam can close it and start acs.exe again. The
                // race is over when no game process is left, and it lasted until the last one exited.
                lastExit = await WaitForRelaunchesAsync(_currentProcessName ?? AcProcesses.Race, DateTime.Now, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Stopped, and the game did not go within the timeout: the race is settled once it has
                _logger.Error("Assetto Corsa did not close within {Seconds}s of being stopped", StopTimeout.TotalSeconds);
                var stuck = Failure(intent, "Assetto Corsa did not close when it was stopped.", startTime);
                if (context != null)
                {
                    stuck.Outcome = RaceOutcome.ResultPending;
                    stuck.PlayerMessages.Add(new PlayerMessage("Race Not Settled Yet",
                        "Assetto Corsa did not close when the race was stopped. The race is settled once it has closed; nothing is lost in the meantime."));
                }

                if (run.EarlierMessages != null) stuck.PlayerMessages.InsertRange(0, run.EarlierMessages);
                return (stuck, false);
            }

            var endTime = DateTime.Now;
            var ranFor = lastExit - processStarted;
            _logger.Information("Assetto Corsa exited. ExitCode: {ExitCode}, Ran: {Ran}s, Duration: {Duration}s",
                exitCode, ranFor.TotalSeconds, (endTime - startTime).TotalSeconds);

            // A game that closed at once never loaded (unless the player stopped it): a launch failure, not a race.
            // Timed on the whole run, relaunches included: a Steam relaunch's first process lives a few seconds.
            var launchFailed = !_stopRequested && ranFor < ShortestRace && intent is DragRaceLaunchIntent;
            if (launchFailed)
                _logger.Error("Assetto Corsa closed {Seconds:F1}s after it started (exit code {ExitCode}): it most likely failed to load", ranFor.TotalSeconds, exitCode);

            var result = LaunchResult.CreateSuccess(startTime, endTime, exitCode);
            result.Outcome = RaceOutcome.NotARace;
            if (run.EarlierMessages != null) result.PlayerMessages.AddRange(run.EarlierMessages);

            // PHASE 5.5: RACE RESULT INGESTION (only for race launches)
            var raceCompleted = false;
            if (intent is DragRaceLaunchIntent)
            {
                raceCompleted = await IngestAsync(context, result, launchFailed);
            }

            _logger.Information("=== LAUNCH PIPELINE COMPLETED ({Outcome}) ===", result.Outcome);
            return (result, raceCompleted);
        }

        /// <summary>
        /// Reads the race's results into the game and says what came of it. A result that is there but cannot be
        /// read yet is tried again, then left pending (never forfeited). With nothing of this race to read, the
        /// player either quit or stopped the race (a wager race is then forfeited) or the game never ran (nothing
        /// happens). True when the game state changed.
        /// </summary>
        private async Task<bool> IngestAsync(RaceContext? context, LaunchResult result, bool launchFailed)
        {
            _logger.Information("PHASE: Race Result Ingestion");

            var processed = 0;
            var quarantinedForContext = 0;
            var retryLater = false;
            for (var attempt = 1; attempt <= IngestionAttempts; attempt++)
            {
                Race.IngestionResult ingestion;
                try
                {
                    ingestion = await _raceResultService.IngestResultsAsync(context);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Race result ingestion failed (attempt {Attempt})", attempt);
                    retryLater = true;
                    ingestion = new Race.IngestionResult { InboxUnreadable = true };
                }

                _logger.Information("Ingestion complete: Processed={Processed}, Duplicates={Duplicates}, Quarantined={Quarantined} ({Ours} of this race), Deferred={Deferred}, Errors={Errors}",
                    ingestion.FilesProcessed, ingestion.FilesDuplicate, ingestion.FilesQuarantined,
                    ingestion.FilesQuarantinedForContext, ingestion.FilesDeferred, ingestion.Errors.Count);
                result.PlayerMessages.AddRange(ingestion.PlayerMessages);

                processed += ingestion.FilesProcessed;
                quarantinedForContext += ingestion.FilesQuarantinedForContext;
                retryLater = ingestion.RetryLater;
                if (processed > 0 || !retryLater || attempt == IngestionAttempts) break;

                _logger.Warning("A result file could not be dealt with yet: trying again in {Seconds}s", IngestionRetryDelay.TotalSeconds);
                await Task.Delay(IngestionRetryDelay);
            }

            if (processed > 0)
            {
                result.Outcome = RaceOutcome.Processed;
                return true;
            }

            if (retryLater)
            {
                // A file is there (or may be): the race is not "without a result". It stays pending in the save.
                result.Outcome = RaceOutcome.ResultPending;
                result.PlayerMessages.Add(new PlayerMessage("Race Result Waiting",
                    "The race's result is there but could not be read just now. Nothing is lost: it counts as soon as the game can read it (when this save is loaded again, or before your next race)."));
                return false;
            }

            if (quarantinedForContext > 0)
            {
                result.Outcome = RaceOutcome.Quarantined;
                result.PlayerMessages.Add(new PlayerMessage("Race Results",
                    "The race wrote a result the game could not use, so nothing was changed. The file is kept for a look; the details are in the log."));
                if (context != null) await ReleaseAsync(context);
                return false;
            }

            if (launchFailed)
            {
                result.Success = false;
                result.Outcome = RaceOutcome.Failed;
                result.ErrorMessage = "Assetto Corsa closed right after it started; it most likely failed to load. Nothing was raced.";
                if (context != null) await ReleaseAsync(context);
                return false;
            }

            result.Outcome = RaceOutcome.NoResult;
            if (context == null)
            {
                _logger.Warning("The race ended without a result and without a race context: nothing to apply");
                return false;
            }

            // The wait for the game can end early (the processes could not be listed): a game still running may
            // still write the result, so nothing is settled under it
            if (AcProcesses.QueryAnyRunning() != false)
            {
                _logger.Warning("No result yet, but Assetto Corsa is (or may be) still running: the race is settled once it has closed");
                result.Outcome = RaceOutcome.ResultPending;
                result.PlayerMessages.Add(new PlayerMessage("Race Not Settled Yet",
                    "Assetto Corsa is still running. The race is settled once it has closed; nothing is lost in the meantime."));
                return false;
            }

            try
            {
                // The forfeit's own message (what was lost) comes from the ingestion service
                var forfeited = await _raceResultService.ApplyNoResultForfeitAsync(context, result.PlayerMessages);
                _logger.Information("The race ended without a result; forfeit applied: {Forfeited}", forfeited);
                if (!forfeited)
                    result.PlayerMessages.Add(new PlayerMessage("No Result", "The race ended before the finish, so it does not count."));

                return forfeited;
            }
            catch (Exception ex)
            {
                // Nothing was changed (the processor puts the state back); the race stays pending and the next pass
                // settles it
                _logger.Error(ex, "Could not apply the forfeit of a race without a result");
                result.Outcome = RaceOutcome.ResultPending;
                result.PlayerMessages.Add(new PlayerMessage("Race Not Settled Yet",
                    "The race ended without a result, but that could not be recorded just now. It is settled the next time this save is loaded, or before your next race."));
                return false;
            }
        }

        /// <summary>
        /// A launch that ends before AC started: the race did not happen, so it is no longer pending in the save
        /// </summary>
        private async Task<LaunchResult> NotStartedAsync(Run run, LaunchResult result)
        {
            if (run.Context != null && run.Began)
                await ReleaseAsync(run.Context);
            if (run.EarlierMessages != null) result.PlayerMessages.InsertRange(0, run.EarlierMessages);
            return result;
        }

        /// <summary>The race is no longer pending; logged, never throws</summary>
        private async Task ReleaseAsync(RaceContext context)
        {
            try
            {
                await _raceResultService.ReleasePendingRaceAsync(context);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not release the pending race {ContextId}", context.ContextId);
            }
        }

        /// <summary>
        /// Waits while any process of that name runs, watching a moment longer after the last one for one that is
        /// being relaunched. Returns when the last of them exited (<paramref name="firstExit"/> when there was none).
        /// </summary>
        private async Task<DateTime> WaitForRelaunchesAsync(string processName, DateTime firstExit, CancellationToken cancellationToken)
        {
            var lastExit = firstExit;
            var quietSince = DateTime.UtcNow;
            while (DateTime.UtcNow - quietSince < RelaunchGrace)
            {
                Process[] running;
                try
                {
                    running = Process.GetProcessesByName(processName);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Could not look for other {Process} processes", processName);
                    return lastExit;
                }

                if (running.Length == 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                    continue;
                }

                try
                {
                    _logger.Information("{Count} other {Process} process(es) still run (a relaunch?): waiting for them", running.Length, processName);
                    foreach (var process in running) await process.WaitForExitAsync(cancellationToken);
                }
                finally
                {
                    foreach (var process in running) process.Dispose();
                }

                lastExit = DateTime.Now;
                quietSince = DateTime.UtcNow;
            }

            return lastExit;
        }

        /// <summary>Car data, copies and cfg files back as they were; each part on its own, logged, never throws</summary>
        private int RestoreInstall()
        {
            var restored = 0;
            try
            {
                var cars = _carData.RestoreAll();
                if (cars > 0) _logger.Information("Data of {Count} car(s) put back, copies taken away", cars);
                restored += cars;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not put the cars' data back");
            }

            try
            {
                var files = _iniService.RestoreAll();
                if (files > 0) _logger.Information("{Count} cfg file(s) put back", files);
                restored += files;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not put the cfg files back");
            }

            return restored;
        }

        private static LaunchResult Failure(LaunchIntent intent, string error, DateTime startTime)
        {
            var result = LaunchResult.CreateFailure(error, startTime, DateTime.Now);
            result.Outcome = intent is DragRaceLaunchIntent ? RaceOutcome.Failed : RaceOutcome.NotARace;
            return result;
        }

        private static LaunchResult Cancelled(LaunchIntent intent, DateTime startTime)
        {
            var result = LaunchResult.CreateFailure("The launch was stopped before Assetto Corsa started. Nothing was raced.", startTime, DateTime.Now);
            result.Outcome = intent is DragRaceLaunchIntent ? RaceOutcome.Cancelled : RaceOutcome.NotARace;
            return result;
        }

        private void RaiseSafely(string name, Action raise)
        {
            try
            {
                raise();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "A {Event} handler failed", name);
            }
        }

        /// <summary>
        /// Get full path to AC executable
        /// </summary>
        private string GetExecutablePath(string executable)
        {
            return Path.Combine(AppSettings.Instance.AssettoCorsaPath, executable);
        }
    }
}
