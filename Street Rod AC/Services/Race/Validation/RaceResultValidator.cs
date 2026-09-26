using Newtonsoft.Json;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Race;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Street_Rod_AC.Services.Race.Validation
{
    /// <summary>
    /// Validator for race result JSON files
    /// Implements strict validation rules for race result JSON files
    /// </summary>
    public class RaceResultValidator : IRaceResultValidator
    {
        private readonly IAppLogger _logger;

        // UUID regex pattern for filename validation
        // Format: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx.json
        private static readonly Regex UuidFilenamePattern = new Regex(
            @"^[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}\.json$",
            RegexOptions.Compiled);

        /// <summary>
        /// The largest result file that is read. The race mode writes a few kilobytes; anything near this is not
        /// its result, and reading it whole into memory is not worth the risk.
        /// </summary>
        public const long MaxFileBytes = 1024 * 1024;

        public RaceResultValidator()
        {
            _logger = AppLoggerFactory.CreateLogger(LogCategory.RaceIngestion);
        }

        public async Task<ValidationResult> ValidateFileAsync(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);

                // FIRST-PASS FILTERING: File-level validation
                var fileValidation = ValidateFileLevel(fileInfo);
                if (!fileValidation.IsValid)
                    return fileValidation;

                // Read file content
                string jsonContent;
                try
                {
                    jsonContent = await File.ReadAllTextAsync(filePath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Locked, or access denied for now (a scanner, a sync client): tried again, never quarantined
                    _logger.Warning("Cannot read file {FilePath}: {Error}", filePath, ex.Message);
                    return ValidationResult.Failure(
                        ValidationFailureReason.FileUnreadable,
                        $"File is locked or unreadable: {ex.Message}");
                }

                // CONTENT VALIDATION: Parse and validate JSON
                RaceResultJson? raceResult;
                try
                {
                    raceResult = JsonConvert.DeserializeObject<RaceResultJson>(jsonContent);
                    if (raceResult == null)
                    {
                        return ValidationResult.Failure(
                            ValidationFailureReason.InvalidJson,
                            "JSON deserialization returned null");
                    }
                }
                catch (JsonException ex)
                {
                    _logger.Warning("Invalid JSON in file {FileName}: {Error}", fileInfo.Name, ex.Message);
                    return ValidationResult.Failure(
                        ValidationFailureReason.InvalidJson,
                        $"Invalid JSON syntax: {ex.Message}");
                }

                // Validate required fields and content
                var contentValidation = ValidateContent(raceResult);
                if (!contentValidation.IsValid)
                    return contentValidation;

                // BUSINESS VALIDATION: Check participant count for drag races
                var businessValidation = ValidateBusinessRules(raceResult);
                if (!businessValidation.IsValid)
                    return businessValidation;

                // All validation passed
                _logger.Debug("File {FileName} passed all validation checks", fileInfo.Name);
                return ValidationResult.Success(raceResult);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unexpected error during validation of {FilePath}", filePath);
                return ValidationResult.Failure(
                    ValidationFailureReason.InvalidJson,
                    $"Unexpected validation error: {ex.Message}");
            }
        }

        public bool IsValidFilename(string filename)
        {
            return UuidFilenamePattern.IsMatch(filename);
        }

        /// <summary>
        /// First-pass file-level validation (before reading content)
        /// </summary>
        private ValidationResult ValidateFileLevel(FileInfo fileInfo)
        {
            // Check extension and UUID pattern
            if (!IsValidFilename(fileInfo.Name))
            {
                _logger.Debug("File {FileName} does not match UUID pattern - ignoring", fileInfo.Name);
                return ValidationResult.Failure(
                    ValidationFailureReason.InvalidFileName,
                    "Filename does not match UUID pattern");
            }

            // Check file size
            if (fileInfo.Length == 0)
            {
                _logger.Debug("File {FileName} has zero size - ignoring", fileInfo.Name);
                return ValidationResult.Failure(
                    ValidationFailureReason.ZeroSize,
                    "File is empty (0 bytes)");
            }

            if (fileInfo.Length > MaxFileBytes)
            {
                _logger.Warning("File {FileName} is {Bytes} bytes, more than a result file can be - not read", fileInfo.Name, fileInfo.Length);
                return ValidationResult.Failure(
                    ValidationFailureReason.TooLarge,
                    $"File is {fileInfo.Length} bytes, over the {MaxFileBytes} byte limit for a race result");
            }

            return new ValidationResult { IsValid = true };
        }

        /// <summary>
        /// Validate JSON content and required fields
        /// </summary>
        private ValidationResult ValidateContent(RaceResultJson raceResult)
        {
            // Check metadata.schema_version
            if (string.IsNullOrWhiteSpace(raceResult.Metadata?.SchemaVersion))
            {
                return ValidationResult.Failure(
                    ValidationFailureReason.MissingSchemaVersion,
                    "Missing metadata.schema_version field");
            }

            // Check metadata.source (must be exactly "sr_race_manager")
            if (raceResult.Metadata.Source != "sr_race_manager")
            {
                _logger.Debug("File has wrong source identifier: {Source} - ignoring", raceResult.Metadata.Source);
                return ValidationResult.Failure(
                    ValidationFailureReason.WrongSource,
                    $"Wrong source identifier: '{raceResult.Metadata.Source}' (expected 'sr_race_manager')");
            }

            // Check session.session_id
            if (string.IsNullOrWhiteSpace(raceResult.Session?.SessionId))
            {
                return ValidationResult.Failure(
                    ValidationFailureReason.MissingSessionId,
                    "Missing session.session_id field");
            }

            // Validate session_id is a valid UUID format
            if (!Guid.TryParse(raceResult.Session.SessionId, out _))
            {
                return ValidationResult.Failure(
                    ValidationFailureReason.MissingSessionId,
                    $"session.session_id is not a valid UUID: '{raceResult.Session.SessionId}'");
            }

            // Check session.start_timestamp
            if (string.IsNullOrWhiteSpace(raceResult.Session?.StartTimestamp))
            {
                return ValidationResult.Failure(
                    ValidationFailureReason.MissingStartTimestamp,
                    "Missing session.start_timestamp field");
            }

            // Parsed here, before anything is applied: the processor must not find a bad date halfway through
            if (!TryParseTimestamp(raceResult.Session.StartTimestamp, out _))
            {
                return ValidationResult.Failure(
                    ValidationFailureReason.InvalidTimestamp,
                    $"session.start_timestamp is not an ISO 8601 date: '{raceResult.Session.StartTimestamp}'");
            }

            // Check participants array exists
            if (raceResult.Participants == null || raceResult.Participants.Count == 0)
            {
                return ValidationResult.Failure(
                    ValidationFailureReason.InvalidParticipantCount,
                    "Participants array is null or empty");
            }

            return new ValidationResult { IsValid = true };
        }

        /// <summary>
        /// Validate business rules (participant count, etc.)
        /// </summary>
        private ValidationResult ValidateBusinessRules(RaceResultJson raceResult)
        {
            // A race has the player and one rival; a test-and-tune the player alone
            var expected = raceResult.Session.RaceType == RaceTypes.TestAndTune ? 1 : 2;
            if (raceResult.Participants.Count != expected)
            {
                _logger.Warning("Expected {Expected} participant(s), found {Count}",
                    expected, raceResult.Participants.Count);
                return ValidationResult.Failure(
                    ValidationFailureReason.InvalidParticipantCount,
                    $"Expected {expected} participant(s), found {raceResult.Participants.Count}");
            }

            // Validate that participants have names
            foreach (var participant in raceResult.Participants)
            {
                if (string.IsNullOrWhiteSpace(participant.DriverName))
                {
                    return ValidationResult.Failure(
                        ValidationFailureReason.InvalidJson,
                        "Participant has missing or empty driver_name");
                }

                // An explicit "performance": null or "crash": null replaces the initializers (Newtonsoft
                // assigns the null), and the processor reads both
                if (participant.Performance == null || participant.Crash == null)
                {
                    return ValidationResult.Failure(
                        ValidationFailureReason.InvalidParticipantData,
                        $"Participant '{participant.DriverName}' has no performance or crash data");
                }

                // The distance goes onto the car's odometer and into the save
                var distance = participant.Performance.DistanceKm;
                if (!double.IsFinite(distance) || distance < 0)
                {
                    return ValidationResult.Failure(
                        ValidationFailureReason.InvalidParticipantData,
                        $"Participant '{participant.DriverName}' has an impossible distance: {distance}");
                }
            }

            if (raceResult.Participants.Count(p => p.IsPlayer == true) > 1)
            {
                return ValidationResult.Failure(
                    ValidationFailureReason.InvalidParticipantData,
                    "More than one participant is marked as the player");
            }

            return new ValidationResult { IsValid = true };
        }

        /// <summary>
        /// The one parser for the Lua app's timestamps ("2026-09-24T18:30:05Z"): culture-free, and a trailing Z
        /// comes back as UTC
        /// </summary>
        public static bool TryParseTimestamp(string? text, out DateTime timestamp) =>
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp);
    }
}
