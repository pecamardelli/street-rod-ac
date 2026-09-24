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

        private readonly IIniModificationService _iniService = iniService;
        private readonly Race.IRaceResultIngestionService _raceResultService = raceResultService;
        private readonly CarDataOverlay _carData = carData;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("ACLauncher");
        private readonly SemaphoreSlim _executionLock = new(1, 1);

        private Process? _currentProcess;
        private string? _currentProcessName;
        private CancellationTokenSource? _monitorCts;
        private volatile bool _stopRequested;
        private bool _isExecutionLocked;

        public bool IsExecutionLocked => _isExecutionLocked;

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

        public async Task<LaunchResult> LaunchShowroomAsync(ShowroomLaunchIntent intent)
        {
            return await ExecuteLaunchPipeline(intent);
        }

        public async Task<LaunchResult> LaunchRaceAsync(LaunchIntent intent)
        {
            return await ExecuteLaunchPipeline(intent);
        }

        /// <summary>
        /// Stops the running game: the process started and its children, and any other AC race process (a relaunch).
        /// The pipeline then carries on as after any exit: results are read (a race stopped before the finish has
        /// none, so a wager race is forfeited) and the install is put back in the finally.
        /// </summary>
        public void CancelRace()
        {
            if (!_isExecutionLocked) return;

            _logger.Warning("Stopping Assetto Corsa at the player's request");
            _stopRequested = true;

            var process = _currentProcess;
            try
            {
                if (process != null && !process.HasExited) process.Kill(entireProcessTree: true);
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
        /// </summary>
        public async Task<int> RestoreWhenAcExitsAsync(CancellationToken cancellationToken = default)
        {
            await _executionLock.WaitAsync(cancellationToken);
            _isExecutionLocked = true;
            try
            {
                while (AcProcesses.AnyRunning())
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

                return RestoreInstall();
            }
            finally
            {
                _isExecutionLocked = false;
                _executionLock.Release();
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
                return LaunchResult.CreateFailure(error, startTime, DateTime.Now);
            }

            _isExecutionLocked = true;
            _stopRequested = false;
            _monitorCts = new CancellationTokenSource();

            LaunchResult result;
            var raceCompleted = false;

            try
            {
                (result, raceCompleted) = await RunAsync(intent, startTime, _monitorCts.Token);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Launch pipeline failed with exception");
                result = LaunchResult.CreateFailure(ex.Message, startTime, DateTime.Now);
                result.Outcome = intent is DragRaceLaunchIntent ? RaceOutcome.Failed : RaceOutcome.NotARace;
                _logger.Information("=== LAUNCH PIPELINE FAILED ===");
            }
            finally
            {
                // Always put the install back, release lock and cleanup. Not under a running game, though: it may
                // be reading the data and holding the sound bank; then it waits for the next start or launch.
                if (AcProcesses.AnyRunning())
                {
                    _logger.Error("Assetto Corsa is still running: the cars' data and the cfg files stay changed until the next start");
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
                _isExecutionLocked = false;
                _executionLock.Release();
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

        /// <summary>The pipeline between the lock and the cleanup: prepare, start, wait, read the results</summary>
        private async Task<(LaunchResult Result, bool RaceCompleted)> RunAsync(LaunchIntent intent, DateTime startTime, CancellationToken cancellationToken)
        {
            // PHASE 2: PREPARE CONFIGURATION
            _logger.Information("PHASE: Prepare Configuration");
            var configIntents = intent.GetConfigurationIntents().ToList();
            _logger.Information("Applying {Count} configuration intent(s)", configIntents.Count);

            foreach (var configIntent in configIntents)
            {
                _logger.Debug("Applying: {Description}", configIntent.Description);

                var success = _iniService.ApplyIntent(configIntent);
                if (!success)
                {
                    var error = $"Failed to apply configuration intent: {configIntent.Description}";
                    _logger.Error(error);
                    return (Failure(intent, error, startTime), false);
                }
            }

            _logger.Information("Configuration prepared successfully");

            // The garage's engines go quiet and let go of their banks: the game is about to sound its own, and a bank
            // that is open cannot be moved aside for the sound a car races with
            await Audio.EngineAudio.Shared.UnloadAllAsync();

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
                var error = $"Executable not found: {exePath}";
                _logger.Error(error);
                return (Failure(intent, error, startTime), false);
            }

            _logger.Information("Executable validated: {ExePath}", exePath);

            // PHASE 4: EXECUTE
            _logger.Information("PHASE: Execute");
            _logger.Information("Launching: {Executable}", intent.Executable);

            RaiseSafely("ExecutionStarted", () => ExecutionStarted?.Invoke(this, intent));

            var processStartInfo = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = AppSettings.Instance.AssettoCorsaPath,
                UseShellExecute = true
            };

            _currentProcessName = AcProcesses.NameOf(intent.Executable);
            _currentProcess = Process.Start(processStartInfo);
            if (_currentProcess == null)
            {
                var error = "Failed to start process";
                _logger.Error(error);
                return (Failure(intent, error, startTime), false);
            }

            var processStarted = DateTime.Now;
            _logger.Information("Process started. PID: {ProcessId}", _currentProcess.Id);

            // PHASE 5: MONITOR
            // For races, the Lua race manager will call ac.shutdownAssettoCorsa() when done
            _logger.Information("PHASE: Monitor");
            _logger.Information("Waiting for Assetto Corsa to exit...");

            await _currentProcess.WaitForExitAsync(cancellationToken);
            var exitCode = _currentProcess.ExitCode;
            var processEnd = DateTime.Now;

            // The process we started is not always the game: Steam can close it and start acs.exe again. The race
            // is over when no game process is left.
            await WaitForRelaunchesAsync(_currentProcessName, cancellationToken);

            var endTime = DateTime.Now;
            var ranFor = processEnd - processStarted;
            _logger.Information("Assetto Corsa exited. ExitCode: {ExitCode}, Duration: {Duration}s",
                exitCode, (endTime - startTime).TotalSeconds);

            // A game that closed at once never loaded (unless the player stopped it): a launch failure, not a race
            var launchFailed = !_stopRequested && ranFor < ShortestRace && intent is DragRaceLaunchIntent;
            if (launchFailed)
                _logger.Error("Assetto Corsa closed {Seconds:F1}s after it started (exit code {ExitCode}): it most likely failed to load", ranFor.TotalSeconds, exitCode);

            var result = LaunchResult.CreateSuccess(startTime, endTime, exitCode);
            result.Outcome = RaceOutcome.NotARace;

            // PHASE 5.5: RACE RESULT INGESTION (only for race launches)
            var raceCompleted = false;
            if (intent is DragRaceLaunchIntent)
            {
                raceCompleted = await IngestAsync(intent, result, launchFailed);
            }

            _logger.Information("=== LAUNCH PIPELINE COMPLETED ({Outcome}) ===", result.Outcome);
            return (result, raceCompleted);
        }

        /// <summary>
        /// Reads the race's results into the game and says what came of it. With nothing to read, the player either
        /// quit or stopped the race (a wager race is then forfeited) or the game never ran (nothing happens).
        /// True when the game state changed.
        /// </summary>
        private async Task<bool> IngestAsync(LaunchIntent intent, LaunchResult result, bool launchFailed)
        {
            _logger.Information("PHASE: Race Result Ingestion");

            RaceContext? context = null;
            if (intent.Metadata.TryGetValue("RaceContext", out var ctxObj))
                context = ctxObj as RaceContext;

            Race.IngestionResult ingestion;
            try
            {
                ingestion = await _raceResultService.IngestResultsAsync(context);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Race result ingestion failed - continuing with cleanup");
                result.Outcome = RaceOutcome.Failed;
                result.PlayerMessages.Add(new PlayerMessage("Race Results",
                    "The race results could not be read. Nothing was changed; the details are in the log."));
                return false;
            }

            _logger.Information("Ingestion complete: Processed={Processed}, Duplicates={Duplicates}, Quarantined={Quarantined}, Errors={Errors}",
                ingestion.FilesProcessed, ingestion.FilesDuplicate,
                ingestion.FilesQuarantined, ingestion.Errors.Count);
            result.PlayerMessages.AddRange(ingestion.PlayerMessages);

            if (ingestion.FilesProcessed > 0)
            {
                result.Outcome = RaceOutcome.Processed;
                return true;
            }

            if (ingestion.FilesQuarantined > 0)
            {
                result.Outcome = RaceOutcome.Quarantined;
                result.PlayerMessages.Add(new PlayerMessage("Race Results",
                    "The race wrote a result the game could not use, so nothing was changed. The file is kept for a look; the details are in the log."));
                return false;
            }

            if (launchFailed)
            {
                result.Success = false;
                result.Outcome = RaceOutcome.Failed;
                result.ErrorMessage = "Assetto Corsa closed right after it started; it most likely failed to load. Nothing was raced.";
                return false;
            }

            result.Outcome = RaceOutcome.NoResult;
            if (context == null)
            {
                _logger.Warning("The race ended without a result and without a race context: nothing to apply");
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
                _logger.Error(ex, "Could not apply the forfeit of a race without a result");
                result.Outcome = RaceOutcome.Failed;
                return false;
            }
        }

        /// <summary>
        /// Waits while any process of that name runs, watching a moment longer after the last one for one that is
        /// being relaunched
        /// </summary>
        private async Task WaitForRelaunchesAsync(string processName, CancellationToken cancellationToken)
        {
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
                    return;
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

                quietSince = DateTime.UtcNow;
            }
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
