namespace Street_Rod_AC.Services.Race.Validation
{
    /// <summary>
    /// Service for validating race result JSON files
    /// Implements strict validation rules from ingestion guidelines
    /// </summary>
    public interface IRaceResultValidator
    {
        /// <summary>
        /// Validate a race result file
        /// Performs file-level, content-level, and business validation
        /// </summary>
        /// <param name="filePath">Full path to the JSON file</param>
        /// <returns>Validation result with errors and parsed data</returns>
        Task<ValidationResult> ValidateFileAsync(string filePath);

        /// <summary>
        /// Check if a filename matches the expected UUID pattern
        /// </summary>
        /// <param name="filename">Filename to check (without path)</param>
        /// <returns>True if valid UUID-based .json filename</returns>
        bool IsValidFilename(string filename);
    }

    /// <summary>
    /// Result of validation operation
    /// Contains errors and parsed data if successful
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// Whether the file passed all validation checks
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// List of validation error messages
        /// </summary>
        public List<string> Errors { get; set; } = new();

        /// <summary>
        /// The reason for validation failure (if any)
        /// </summary>
        public ValidationFailureReason? FailureReason { get; set; }

        /// <summary>
        /// Parsed race result (only if IsValid == true)
        /// </summary>
        public Models.Race.RaceResultJson? ParsedResult { get; set; }

        /// <summary>
        /// Create a successful validation result
        /// </summary>
        public static ValidationResult Success(Models.Race.RaceResultJson parsedResult)
        {
            return new ValidationResult
            {
                IsValid = true,
                ParsedResult = parsedResult
            };
        }

        /// <summary>
        /// Create a failed validation result
        /// </summary>
        public static ValidationResult Failure(ValidationFailureReason reason, string error)
        {
            return new ValidationResult
            {
                IsValid = false,
                FailureReason = reason,
                Errors = new List<string> { error }
            };
        }
    }

    /// <summary>
    /// Enumeration of validation failure reasons
    /// Used to determine appropriate error handling (ignore, quarantine, retry)
    /// </summary>
    public enum ValidationFailureReason
    {
        /// <summary>
        /// Filename does not match UUID pattern or wrong extension
        /// Action: Completely ignore
        /// </summary>
        InvalidFileName,

        /// <summary>
        /// File size is zero bytes
        /// Action: Completely ignore
        /// </summary>
        ZeroSize,

        /// <summary>
        /// File cannot be read (locked or permissions issue)
        /// Action: Skip and retry later
        /// </summary>
        FileUnreadable,

        /// <summary>
        /// File contains invalid JSON syntax
        /// Action: Quarantine
        /// </summary>
        InvalidJson,

        /// <summary>
        /// Missing metadata.schema_version field
        /// Action: Quarantine
        /// </summary>
        MissingSchemaVersion,

        /// <summary>
        /// metadata.source is not "StreetRodRaceApp"
        /// Action: Completely ignore (not our file)
        /// </summary>
        WrongSource,

        /// <summary>
        /// Missing or invalid session.session_id
        /// Action: Quarantine
        /// </summary>
        MissingSessionId,

        /// <summary>
        /// Missing session.start_timestamp
        /// Action: Quarantine
        /// </summary>
        MissingStartTimestamp,

        /// <summary>
        /// Session ID has already been processed (duplicate)
        /// Action: Delete file
        /// </summary>
        DuplicateSessionId,

        /// <summary>
        /// Participants array does not have exactly 2 entries for drag race
        /// Action: Quarantine
        /// </summary>
        InvalidParticipantCount
    }
}
