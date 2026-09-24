using Street_Rod_AC.Models.Race;

namespace Street_Rod_AC.Services.Race
{
    /// <summary>
    /// Service for ingesting race result files from AC Lua app (sr_race_manager)
    /// Implements the 8-step ingestion pipeline from guidelines
    /// </summary>
    public interface IRaceResultIngestionService
    {
        /// <summary>
        /// Ingest race results from the AC output folder into the loaded game. A file is applied only with the
        /// context it belongs to (its context_id); anything else is quarantined, never applied to this race.
        /// Needs a loaded game: without one the files stay where they are.
        /// </summary>
        /// <param name="raceContext">The race just run; null uses the save's pending race</param>
        /// <param name="progress">Optional progress reporter</param>
        /// <returns>Ingestion summary</returns>
        Task<IngestionResult> IngestResultsAsync(
            RaceContext? raceContext = null,
            IProgress<string>? progress = null);

        /// <summary>
        /// Applies result files left from a race whose result never came in (the app closed during it) to the
        /// loaded game: only a file of the save's pending race (<c>GameState.PendingRace</c>). Call it once a
        /// game is loaded; without one it does nothing.
        /// </summary>
        Task<IngestionResult> ProcessOrphanedResultsAsync();

        /// <summary>
        /// A race that brought back no result. With money or a pink slip on it the player walked away, which
        /// counts as a loss: it is recorded, saved, and the pending race cleared. Without stakes the pending
        /// race is only cleared.
        /// </summary>
        /// <param name="context">The race that was run</param>
        /// <param name="messages">Receives what the player should be told, when given</param>
        /// <returns>True when a forfeit was applied</returns>
        Task<bool> ApplyNoResultForfeitAsync(RaceContext context, List<PlayerMessage>? messages = null);
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
        /// What the player should be told about the races that were applied (event rewards, career progress)
        /// </summary>
        public List<PlayerMessage> PlayerMessages { get; } = new();

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
