using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Street_Rod_AC.Configuration;
using Street_Rod_AC.Logging;
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
        Race.IRaceResultIngestionService raceResultService) : IAssettoCorsaLauncher
    {
        // Windows API for input simulation
        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private const byte VK_RETURN = 0x0D;  // Enter key
        private const byte VK_ESCAPE = 0x1B;  // Escape key
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private readonly IIniModificationService _iniService = iniService;
        private readonly Race.IRaceResultIngestionService _raceResultService = raceResultService;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("ACLauncher");
        private readonly SemaphoreSlim _executionLock = new(1, 1);
        private readonly List<string> _modifiedFiles = [];

        private Process? _currentProcess;
        private bool _isExecutionLocked;

        public bool IsExecutionLocked => _isExecutionLocked;
        public bool IsAssettoCorsaRunning => _currentProcess != null && !_currentProcess.HasExited;

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
            _modifiedFiles.Clear();

            try
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

                        // Restore and abort
                        await RestoreConfiguration();

                        // Delete backups for showroom launches
                        if (intent is ShowroomLaunchIntent)
                        {
                            foreach (var modifiedFile in _modifiedFiles)
                            {
                                _iniService.DeleteBackups(modifiedFile);
                            }
                        }

                        return LaunchResult.CreateFailure(error, startTime, DateTime.Now);
                    }

                    _modifiedFiles.Add(configIntent.TargetFile);
                }

                _logger.Information("Configuration prepared successfully");

                // PHASE 2.5: ENABLE STREET ROD RACE MODE (only for races)
                if (intent is DragRaceLaunchIntent or RaceLaunchIntent)
                {
                    _logger.Information("PHASE: Enable Street Rod Race Mode");
                    EnableStreetRodRaceApp();
                    EnableCspRaceMode();
                }

                // PHASE 3: VALIDATE EXECUTABLE
                _logger.Information("PHASE: Validate Executable");
                var exePath = GetExecutablePath(intent.Executable);

                if (!File.Exists(exePath))
                {
                    var error = $"Executable not found: {exePath}";
                    _logger.Error(error);
                    await RestoreConfiguration();

                    // Delete backups for showroom launches
                    if (intent is ShowroomLaunchIntent)
                    {
                        foreach (var modifiedFile in _modifiedFiles)
                        {
                            _iniService.DeleteBackups(modifiedFile);
                        }
                    }

                    return LaunchResult.CreateFailure(error, startTime, DateTime.Now);
                }

                _logger.Information("Executable validated: {ExePath}", exePath);

                // PHASE 4: EXECUTE
                _logger.Information("PHASE: Execute");
                _logger.Information("Launching: {Executable}", intent.Executable);

                ExecutionStarted?.Invoke(this, intent);

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = AppSettings.Instance.AssettoCorsaPath,
                    UseShellExecute = true
                };

                _currentProcess = Process.Start(processStartInfo);
                if (_currentProcess == null)
                {
                    var error = "Failed to start process";
                    _logger.Error(error);
                    await RestoreConfiguration();

                    // Delete backups for showroom launches
                    if (intent is ShowroomLaunchIntent)
                    {
                        foreach (var modifiedFile in _modifiedFiles)
                        {
                            _iniService.DeleteBackups(modifiedFile);
                        }
                    }

                    return LaunchResult.CreateFailure(error, startTime, DateTime.Now);
                }

                _logger.Information("Process started. PID: {ProcessId}", _currentProcess.Id);

                // PHASE 5: MONITOR (with quit signal detection for races)
                _logger.Information("PHASE: Monitor");
                _logger.Information("Waiting for Assetto Corsa to exit...");

                if (intent is DragRaceLaunchIntent or RaceLaunchIntent)
                {
                    // For races, monitor for quit signal from the Python app
                    await WaitForExitOrQuitSignalAsync(_currentProcess);
                }
                else
                {
                    // For showroom, just wait normally
                    await _currentProcess.WaitForExitAsync();
                }

                var exitCode = _currentProcess.ExitCode;
                var endTime = DateTime.Now;

                _logger.Information("Assetto Corsa exited. ExitCode: {ExitCode}, Duration: {Duration}s",
                    exitCode, (endTime - startTime).TotalSeconds);

                // PHASE 5.5: RACE RESULT INGESTION (only for race launches)
                var raceCompleted = false;
                if (intent is DragRaceLaunchIntent dragIntent)
                {
                    _logger.Information("PHASE: Race Result Ingestion");

                    // Extract race context from metadata
                    Models.Race.RaceContext? context = null;
                    if (intent.Metadata.TryGetValue("RaceContext", out var ctxObj))
                        context = ctxObj as Models.Race.RaceContext;

                    try
                    {
                        // Ingest results (blocks until complete)
                        var ingestionResult = await _raceResultService.IngestResultsAsync(context);

                        _logger.Information("Ingestion complete: Processed={Processed}, Duplicates={Duplicates}, Quarantined={Quarantined}, Errors={Errors}",
                            ingestionResult.FilesProcessed, ingestionResult.FilesDuplicate,
                            ingestionResult.FilesQuarantined, ingestionResult.Errors.Count);

                        raceCompleted = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Race result ingestion failed - continuing with cleanup");
                    }

                    // Disable Street Rod Race Mode after race completes
                    _logger.Information("PHASE: Disable Street Rod Race Mode");
                    DisableStreetRodRaceApp();
                    DisableCspRaceMode();
                }

                // PHASE 6: CLEANUP
                _logger.Information("PHASE: Cleanup");
                await RestoreConfiguration();

                // Delete backups for showroom launches
                if (intent is ShowroomLaunchIntent)
                {
                    _logger.Information("Deleting showroom backup files");
                    foreach (var modifiedFile in _modifiedFiles)
                    {
                        _iniService.DeleteBackups(modifiedFile);
                    }
                }

                var result = LaunchResult.CreateSuccess(startTime, endTime, exitCode);
                result.ModifiedFiles.AddRange(_modifiedFiles);

                _logger.Information("=== LAUNCH PIPELINE COMPLETED SUCCESSFULLY ===");

                ExecutionEnded?.Invoke(this, result);

                // Signal that race completed (game state has been updated)
                if (raceCompleted)
                {
                    _logger.Information("Raising RaceCompleted event");
                    RaceCompleted?.Invoke(this, EventArgs.Empty);
                }

                return result;
            }
            catch (Exception ex)
            {
                var endTime = DateTime.Now;
                _logger.Error(ex, "Launch pipeline failed with exception");

                // Always restore on error
                await RestoreConfiguration();

                // Delete backups for showroom launches even on error
                if (intent is ShowroomLaunchIntent)
                {
                    _logger.Information("Deleting showroom backup files after error");
                    foreach (var modifiedFile in _modifiedFiles)
                    {
                        _iniService.DeleteBackups(modifiedFile);
                    }
                }

                var result = LaunchResult.CreateFailure(ex.Message, startTime, endTime);
                result.ModifiedFiles.AddRange(_modifiedFiles);

                _logger.Information("=== LAUNCH PIPELINE FAILED ===");

                ExecutionEnded?.Invoke(this, result);
                return result;
            }
            finally
            {
                // Always release lock and cleanup
                _currentProcess = null;
                _isExecutionLocked = false;
                _executionLock.Release();
            }
        }

        /// <summary>
        /// Send a keystroke to AC to auto-start the race
        /// </summary>
        private async Task AutoStartRaceAsync(Process process)
        {
            try
            {
                // Wait for AC to fully load (adjust delay as needed)
                _logger.Information("Waiting for AC to load before auto-start...");
                await Task.Delay(5000);  // 5 seconds for AC to load

                if (process.HasExited)
                {
                    _logger.Warning("AC exited before auto-start could be sent");
                    return;
                }

                // Bring AC window to foreground
                var mainWindow = process.MainWindowHandle;
                if (mainWindow != IntPtr.Zero)
                {
                    SetForegroundWindow(mainWindow);
                    await Task.Delay(100);
                }

                // Send Enter key to start the race
                _logger.Information("Sending Enter key to auto-start race");
                keybd_event(VK_RETURN, 0, 0, UIntPtr.Zero);  // Key down
                await Task.Delay(50);
                keybd_event(VK_RETURN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);  // Key up

                _logger.Information("Auto-start keystroke sent");
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Auto-start failed - user will need to start manually");
            }
        }

        /// <summary>
        /// Wait for AC to exit or for the Python app to signal quit
        /// </summary>
        private async Task WaitForExitOrQuitSignalAsync(Process process)
        {
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var signalPath = Path.Combine(documentsPath, "Assetto Corsa", "out", "StreetRodRaceApp", "quit_signal");

            // Delete any existing signal file from previous runs
            if (File.Exists(signalPath))
            {
                try { File.Delete(signalPath); } catch { }
            }

            _logger.Information("Monitoring for quit signal at: {SignalPath}", signalPath);

            // Poll for either process exit or quit signal
            while (!process.HasExited)
            {
                // Check for quit signal file
                if (File.Exists(signalPath))
                {
                    _logger.Information("Quit signal detected! Terminating Assetto Corsa...");

                    try
                    {
                        // Kill the process immediately (session output already written by Python app)
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                            _logger.Information("Assetto Corsa process terminated");
                        }

                        // Clean up the signal file
                        File.Delete(signalPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, "Error during quit signal handling");
                    }

                    break;
                }

                // Small delay before next check (fast polling for responsive quit)
                await Task.Delay(50);
            }

            // Ensure process has fully exited
            if (!process.HasExited)
            {
                await process.WaitForExitAsync();
            }
        }

        /// <summary>
        /// Restore all modified configuration files from backup
        /// </summary>
        private async Task RestoreConfiguration()
        {
            _logger.Information("Restoring configuration from backups");

            foreach (var modifiedFile in _modifiedFiles)
            {
                try
                {
                    var success = _iniService.RestoreFromBackup(modifiedFile);
                    if (success)
                    {
                        _logger.Debug("Restored: {FileName}", modifiedFile);
                    }
                    else
                    {
                        _logger.Warning("Failed to restore: {FileName}", modifiedFile);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Error restoring {FileName}", modifiedFile);
                }
            }

            _logger.Information("Configuration restoration completed");
            await Task.CompletedTask;
        }

        /// <summary>
        /// Get full path to AC executable
        /// </summary>
        private string GetExecutablePath(string executable)
        {
            return Path.Combine(AppSettings.Instance.AssettoCorsaPath, executable);
        }

        /// <summary>
        /// Enable the streetrodraceapp Python app
        /// </summary>
        private void EnableStreetRodRaceApp()
        {
            try
            {
                var intent = new Configuration.Models.PythonAppToggleIntent
                {
                    AppName = "streetrodraceapp",
                    Enable = true
                };

                _iniService.ApplyIntent(intent);
                _logger.Information("Street Rod Race App enabled");
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to enable Street Rod Race App - race will continue without it");
            }
        }

        /// <summary>
        /// Disable the streetrodraceapp Python app
        /// </summary>
        private void DisableStreetRodRaceApp()
        {
            try
            {
                var intent = new Configuration.Models.PythonAppToggleIntent
                {
                    AppName = "streetrodraceapp",
                    Enable = false
                };

                _iniService.ApplyIntent(intent);
                _logger.Information("Street Rod Race App disabled");
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to disable Street Rod Race App");
            }
        }

        /// <summary>
        /// Enable the CSP sr_race mode by writing csp_extra_options.ini
        /// This enables auto-start and auto-quit functionality via CSP Lua script
        /// </summary>
        private void EnableCspRaceMode()
        {
            try
            {
                // CSP reads csp_extra_options.ini from Documents/Assetto Corsa/cfg folder
                var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var cspOptionsPath = Path.Combine(
                    documentsPath,
                    "Assetto Corsa",
                    "cfg",
                    "csp_extra_options.ini");

                var sb = new StringBuilder();
                sb.AppendLine("; Street Rod Race - CSP Extra Options");
                sb.AppendLine("; Auto-generated by Street Rod AC launcher");
                sb.AppendLine("; This enables the sr_race mode for auto-start and auto-quit");
                sb.AppendLine();
                sb.AppendLine("[NEW_MODE]");
                sb.AppendLine("NAME=sr_race");

                File.WriteAllText(cspOptionsPath, sb.ToString());
                _logger.Information("CSP race mode enabled: {Path}", cspOptionsPath);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to enable CSP race mode - race will continue without auto-start/quit");
            }
        }

        /// <summary>
        /// Disable the CSP sr_race mode by removing csp_extra_options.ini
        /// </summary>
        private void DisableCspRaceMode()
        {
            try
            {
                // CSP reads csp_extra_options.ini from Documents/Assetto Corsa/cfg folder
                var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var cspOptionsPath = Path.Combine(
                    documentsPath,
                    "Assetto Corsa",
                    "cfg",
                    "csp_extra_options.ini");

                if (File.Exists(cspOptionsPath))
                {
                    File.Delete(cspOptionsPath);
                    _logger.Information("CSP race mode disabled (file removed)");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to disable CSP race mode");
            }
        }
    }
}
