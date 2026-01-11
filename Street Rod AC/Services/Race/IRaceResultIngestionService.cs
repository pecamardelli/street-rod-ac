using Street_Rod_AC.Models.Race;

namespace Street_Rod_AC.Services.Race
{
    /// <summary>
    /// Service for ingesting race result files from AC Python app
    /// Implements the 8-step ingestion pipeline from guidelines
    /// </summary>
    public interface IRaceResultIngestionService
    {
        /// <summary>
        /// Ingest race results from the AC output folder
        /// </summary>
        /// <param name="raceContext">Optional race context for correlation</param>
        /// <param name="progress">Optional progress reporter</param>
        /// <returns>Ingestion summary</returns>
        Task<IngestionResult> IngestResultsAsync(
            RaceContext? raceContext = null,
            IProgress<string>? progress = null);

        /// <summary>
        /// Process orphaned result files on application startup
        /// </summary>
        Task<IngestionResult> ProcessOrphanedResultsAsync();
    }

    /// <summary>
    /// Result of ingestion operation
    /// Contains statistics about files processed
    /// </summary>
    public class IngestionResult
    {
        /// <summary>
        /// Total files scanned in inbox
        /// </summary>
        public int FilesScanned { get; set; }

        /// <summary>
        /// Files successfully processed
        /// </summary>
        public int FilesProcessed { get; set; }

        /// <summary>
        /// Files identified as duplicates
        /// </summary>
        public int FilesDuplicate { get; set; }

        /// <summary>
        /// Files moved to quarantine
        /// </summary>
        public int FilesQuarantined { get; set; }

        /// <summary>
        /// Files completely ignored (non-UUID, foreign data)
        /// </summary>
        public int FilesIgnored { get; set; }

        /// <summary>
        /// List of error messages
        /// </summary>
        public List<string> Errors { get; set; } = new();

        /// <summary>
        /// Total duration of ingestion operation
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Whether the operation was successful (no critical errors)
        /// </summary>
        public bool Success => Errors.Count == 0;
    }
}
