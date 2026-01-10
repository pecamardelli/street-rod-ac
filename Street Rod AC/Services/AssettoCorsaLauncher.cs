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
    public class AssettoCorsaLauncher : IAssettoCorsaLauncher
    {
        private readonly IIniModificationService _iniService;
        private readonly IAppLogger _logger;
        private readonly SemaphoreSlim _executionLock;
        private readonly List<string> _modifiedFiles;

        private Process? _currentProcess;
        private bool _isExecutionLocked;

        public bool IsExecutionLocked => _isExecutionLocked;
        public bool IsAssettoCorsaRunning => _currentProcess != null && !_currentProcess.HasExited;

        public event EventHandler<LaunchIntent>? ExecutionStarted;
        public event EventHandler<LaunchResult>? ExecutionEnded;

        public AssettoCorsaLauncher(IIniModificationService iniService)
        {
            _iniService = iniService;
            _logger = AppLoggerFactory.CreateLogger("ACLauncher");
            _executionLock = new SemaphoreSlim(1, 1);
            _modifiedFiles = new List<string>();
        }

        public async Task<LaunchResult> LaunchShowroomAsync(ShowroomLaunchIntent intent)
        {
            return await ExecuteLaunchPipeline(intent);
        }

        public async Task<LaunchResult> LaunchRaceAsync(RaceLaunchIntent intent)
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
                        return LaunchResult.CreateFailure(error, startTime, DateTime.Now);
                    }

                    _modifiedFiles.Add(configIntent.TargetFile);
                }

                _logger.Information("Configuration prepared successfully");

                // PHASE 3: VALIDATE EXECUTABLE
                _logger.Information("PHASE: Validate Executable");
                var exePath = GetExecutablePath(intent.Executable);

                if (!File.Exists(exePath))
                {
                    var error = $"Executable not found: {exePath}";
                    _logger.Error(error);
                    await RestoreConfiguration();
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

                // PHASE 6: CLEANUP
                _logger.Information("PHASE: Cleanup");
                await RestoreConfiguration();

                var result = LaunchResult.CreateSuccess(startTime, endTime, exitCode);
                result.ModifiedFiles.AddRange(_modifiedFiles);

                _logger.Information("=== LAUNCH PIPELINE COMPLETED SUCCESSFULLY ===");

                ExecutionEnded?.Invoke(this, result);
                return result;
            }
            catch (Exception ex)
            {
                var endTime = DateTime.Now;
                _logger.Error(ex, "Launch pipeline failed with exception");

                // Always restore on error
                await RestoreConfiguration();

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
    }
}
