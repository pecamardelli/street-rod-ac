using Newtonsoft.Json;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Race;
using System.IO;
using System.Text.RegularExpressions;

namespace Street_Rod_AC.Services.Race.Validation
{
    /// <summary>
    /// Validator for race result JSON files
    /// Implements strict validation rules from assetto_corsa_python_app_result_ingestion_guidelines.txt
    /// </summary>
    public class RaceResultValidator : IRaceResultValidator
    {
        private readonly IAppLogger _logger;

        // UUID regex pattern for filename validation
        // Format: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx.json
        private static readonly Regex UuidFilenamePattern = new Regex(
            @"^[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}\.json$",
            RegexOptions.Compiled);

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
                catch (IOException ex)
                {
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

            // Check metadata.source (must be exactly "StreetRodRaceApp")
            if (raceResult.Metadata.Source != "StreetRodRaceApp")
            {
                _logger.Debug("File has wrong source identifier: {Source} - ignoring", raceResult.Metadata.Source);
                return ValidationResult.Failure(
                    ValidationFailureReason.WrongSource,
                    $"Wrong source identifier: '{raceResult.Metadata.Source}' (expected 'StreetRodRaceApp')");
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
            // For drag races, we expect exactly 2 participants
            if (raceResult.Participants.Count != 2)
            {
                _logger.Warning("Expected 2 participants for drag race, found {Count}",
                    raceResult.Participants.Count);
                return ValidationResult.Failure(
                    ValidationFailureReason.InvalidParticipantCount,
                    $"Expected 2 participants, found {raceResult.Participants.Count}");
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
            }

            return new ValidationResult { IsValid = true };
        }
    }
}
