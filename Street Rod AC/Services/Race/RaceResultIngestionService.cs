using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Services.Race.Validation;
using System.IO;

namespace Street_Rod_AC.Services.Race
{
    /// <summary>
    /// Main orchestrator for race result ingestion pipeline
    /// Implements 8-step pipeline from assetto_corsa_python_app_result_ingestion_guidelines.txt
    /// </summary>
    public class RaceResultIngestionService : IRaceResultIngestionService
    {
        private readonly IRaceResultValidator _validator;
        private readonly SessionDeduplicator _deduplicator;
        private readonly IRaceResultProcessor _processor;
        private readonly IRaceSessionRepository _sessionRepository;
        private readonly IAppLogger _logger;

        public RaceResultIngestionService(
            IRaceResultValidator validator,
            SessionDeduplicator deduplicator,
            IRaceResultProcessor processor,
            IRaceSessionRepository sessionRepository)
        {
            _validator = validator;
            _deduplicator = deduplicator;
            _processor = processor;
            _sessionRepository = sessionRepository;
            _logger = AppLoggerFactory.CreateLogger(LogCategory.RaceIngestion);
        }

        public async Task<IngestionResult> IngestResultsAsync(
            RaceContext? raceContext = null,
            IProgress<string>? progress = null)
        {
            var result = new IngestionResult();
            var startTime = DateTime.Now;

            _logger.Information("=== RACE RESULT INGESTION STARTED ===");

            try
            {
                // Step 1: Folder enumeration
                var inboxPath = GetInboxPath();
                if (!Directory.Exists(inboxPath))
                {
                    _logger.Information("Inbox directory does not exist: {Path}", inboxPath);
                    result.Duration = DateTime.Now - startTime;
                    return result;
                }

                var files = Directory.GetFiles(inboxPath);
                result.FilesScanned = files.Length;
                _logger.Information("Scanned {Count} files in inbox: {Path}", files.Length, inboxPath);

                if (files.Length == 0)
                {
                    _logger.Information("No files to process");
                    result.Duration = DateTime.Now - startTime;
                    return result;
                }

                // Step 2: First-pass filtering
                var candidates = files
                    .Select(f => new FileInfo(f))
                    .Where(f => _validator.IsValidFilename(f.Name))
                    .ToList();

                result.FilesIgnored = files.Length - candidates.Count;
                _logger.Information("{Count} candidate files after first-pass filtering ({Ignored} ignored)",
                    candidates.Count, result.FilesIgnored);

                // Steps 3-8: Process each candidate
                foreach (var file in candidates)
                {
                    progress?.Report($"Processing {file.Name}...");
                    await ProcessSingleFileAsync(file, raceContext, result);
                }

                result.Duration = DateTime.Now - startTime;
                _logger.Information("=== RACE RESULT INGESTION COMPLETED ===");
                _logger.Information("Summary: Scanned={Scanned}, Processed={Processed}, Duplicates={Duplicates}, Quarantined={Quarantined}, Ignored={Ignored}, Duration={Duration}s",
                    result.FilesScanned, result.FilesProcessed, result.FilesDuplicate,
                    result.FilesQuarantined, result.FilesIgnored, result.Duration.TotalSeconds);

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Race result ingestion failed with exception");
                result.Errors.Add($"Critical error: {ex.Message}");
                result.Duration = DateTime.Now - startTime;
                return result;
            }
        }

        public async Task<IngestionResult> ProcessOrphanedResultsAsync()
        {
            _logger.Information("Processing orphaned race result files");
            return await IngestResultsAsync(raceContext: null);
        }

        /// <summary>
        /// Process a single candidate file through the pipeline
        /// </summary>
        private async Task ProcessSingleFileAsync(FileInfo file, RaceContext? context, IngestionResult result)
        {
            try
            {
                _logger.Debug("Processing file: {FileName}", file.Name);

                // Step 3: Content validation
                var validation = await _validator.ValidateFileAsync(file.FullName);

                if (!validation.IsValid)
                {
                    HandleValidationFailure(file, validation, result);
                    return;
                }

                var raceResult = validation.ParsedResult!;
                _logger.Debug("File {FileName} validated successfully", file.Name);

                // Step 4: Deduplication check
                if (await _deduplicator.IsProcessedAsync(raceResult.Session.SessionId))
                {
                    _logger.Information("Session {SessionId} already processed - deleting duplicate file",
                        raceResult.Session.SessionId);

                    // Safe cleanup - delete duplicate
                    try
                    {
                        File.Delete(file.FullName);
                        result.FilesDuplicate++;
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning("Failed to delete duplicate file {FileName}: {Error}",
                            file.Name, ex.Message);
                    }

                    return;
                }

                // Step 5: Ownership transfer (atomic move)
                var archivePath = GetArchivePath(raceResult.Session.SessionId);
                try
                {
                    var archiveDir = Path.GetDirectoryName(archivePath);
                    if (!string.IsNullOrEmpty(archiveDir))
                        Directory.CreateDirectory(archiveDir);

                    File.Move(file.FullName, archivePath, overwrite: false);
                    _logger.Debug("Moved {FileName} to archive: {ArchivePath}", file.Name, archivePath);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to move file {FileName} to archive", file.Name);
                    result.Errors.Add($"Move failed for {file.Name}: {ex.Message}");
                    return;
                }

                // Step 6: Second-pass validation
                var revalidation = await _validator.ValidateFileAsync(archivePath);
                if (!revalidation.IsValid)
                {
                    _logger.Error("File {FileName} corrupted during move - quarantining", file.Name);
                    await QuarantineFileAsync(new FileInfo(archivePath), revalidation);
                    result.FilesQuarantined++;
                    return;
                }

                // Step 7: Processing
                try
                {
                    await _processor.ProcessRaceResultAsync(raceResult, context);
                    _logger.Information("Successfully processed session {SessionId}", raceResult.Session.SessionId);
                    result.FilesProcessed++;

                    // Optional: Delete archived file after successful processing
                    // Uncomment if you don't want to keep archived files
                    // File.Delete(archivePath);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to process session {SessionId}", raceResult.Session.SessionId);
                    result.Errors.Add($"Processing failed for {file.Name}: {ex.Message}");

                    // Quarantine the file for investigation
                    await QuarantineFileAsync(new FileInfo(archivePath), validation);
                    result.FilesQuarantined++;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unexpected error processing file {FileName}", file.Name);
                result.Errors.Add($"Unexpected error for {file.Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle validation failure according to failure reason
        /// </summary>
        private void HandleValidationFailure(FileInfo file, ValidationResult validation, IngestionResult result)
        {
            var reason = validation.FailureReason;

            _logger.Warning("File {FileName} failed validation: {Reason} - {Errors}",
                file.Name, reason, string.Join(", ", validation.Errors));

            // Determine action based on failure reason
            switch (reason)
            {
                case ValidationFailureReason.InvalidFileName:
                case ValidationFailureReason.ZeroSize:
                case ValidationFailureReason.WrongSource:
                    // Completely ignore - not our file
                    _logger.Debug("Ignoring file {FileName} (not a Street Rod result file)", file.Name);
                    result.FilesIgnored++;
                    break;

                case ValidationFailureReason.FileUnreadable:
                    // Skip and retry later (file may be locked)
                    _logger.Warning("File {FileName} is unreadable - leaving for retry", file.Name);
                    result.FilesIgnored++;
                    break;

                case ValidationFailureReason.InvalidJson:
                case ValidationFailureReason.MissingSchemaVersion:
                case ValidationFailureReason.MissingSessionId:
                case ValidationFailureReason.MissingStartTimestamp:
                case ValidationFailureReason.InvalidParticipantCount:
                    // Quarantine - looks like our file but invalid
                    _logger.Warning("File {FileName} failed content validation - quarantining", file.Name);
                    QuarantineFileAsync(file, validation).Wait();
                    result.FilesQuarantined++;
                    break;

                default:
                    // Unknown reason - ignore
                    _logger.Warning("File {FileName} failed validation with unknown reason - ignoring", file.Name);
                    result.FilesIgnored++;
                    break;
            }
        }

        /// <summary>
        /// Move a file to quarantine with error sidecar
        /// </summary>
        private async Task QuarantineFileAsync(FileInfo file, ValidationResult validation)
        {
            try
            {
                var quarantinePath = GetQuarantinePath();
                Directory.CreateDirectory(quarantinePath);

                var quarantineFilePath = Path.Combine(quarantinePath, file.Name);
                var errorFilePath = quarantineFilePath + ".error.txt";

                // Move file to quarantine
                if (file.Exists)
                {
                    File.Move(file.FullName, quarantineFilePath, overwrite: true);
                }

                // Create error sidecar file
                var errorMessage = $"Validation failed: {validation.FailureReason}\n" +
                                   $"Errors:\n{string.Join("\n", validation.Errors)}\n" +
                                   $"Quarantined at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

                await File.WriteAllTextAsync(errorFilePath, errorMessage);

                _logger.Debug("Quarantined file {FileName} with error sidecar", file.Name);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to quarantine file {FileName}", file.Name);
            }
        }

        /// <summary>
        /// Get the inbox path (Python app output folder)
        /// </summary>
        private string GetInboxPath()
        {
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(documentsPath, "Assetto Corsa", "out", "StreetRodRaceApp");
        }

        /// <summary>
        /// Get the archive path for a processed session
        /// Organized by player name: %AppData%\StreetRodAC\RaceResults\{PlayerName}\{session_id}.json
        /// </summary>
        private string GetArchivePath(string sessionId)
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            // Get player name from current game state
            var app = System.Windows.Application.Current as App;
            var playerName = app?.CurrentGameState?.Player?.Name ?? "Unknown";

            // Sanitize player name for file system (remove invalid characters)
            var invalidChars = Path.GetInvalidFileNameChars();
            var safePlayerName = string.Join("_", playerName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

            return Path.Combine(appDataPath, "StreetRodAC", "RaceResults", safePlayerName, $"{sessionId}.json");
        }

        /// <summary>
        /// Get the quarantine folder path
        /// </summary>
        private string GetQuarantinePath()
        {
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(documentsPath, "Assetto Corsa", "out", "StreetRodRaceApp_quarantine");
        }
    }
}
