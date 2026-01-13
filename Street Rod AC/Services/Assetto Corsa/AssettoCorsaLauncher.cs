using System.Diagnostics;
using System.IO;
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

                // PHASE 2.5: ENABLE STREET ROD RACE APP (only for races)
                if (intent is DragRaceLaunchIntent or RaceLaunchIntent)
                {
                    _logger.Information("PHASE: Enable Street Rod Race App");
                    EnableStreetRodRaceApp();
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

                // PHASE 5: MONITOR (PASSIVE)
                _logger.Information("PHASE: Monitor (passive)");
                _logger.Information("Waiting for Assetto Corsa to exit...");

                await _currentProcess.WaitForExitAsync();

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

                    // Disable Street Rod Race App after race completes
                    _logger.Information("PHASE: Disable Street Rod Race App");
                    DisableStreetRodRaceApp();
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
    }
}
