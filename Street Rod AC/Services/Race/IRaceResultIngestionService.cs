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
        /// game is loaded, and again once Assetto Corsa has exited; without a game it does nothing, and while a
        /// launch is under way (<see cref="BeginRaceAsync"/> to <see cref="EndRace"/>) it leaves the inbox alone.
        /// When the pending race is still unanswered after the pass and nothing is left to retry: a file of it that
        /// was quarantined voids it; AC still running leaves it for later; a race AC never started for is released;
        /// one that ran and brought back no file at all is forfeited (a wager or pink slip is lost). What the player
        /// should hear is in <see cref="IngestionResult.PlayerMessages"/>.
        /// </summary>
        Task<IngestionResult> ProcessOrphanedResultsAsync();

        /// <summary>
        /// A race launch starts. An earlier race of this save that is still pending is settled first (its file
        /// applied, or the rule of <see cref="ProcessOrphanedResultsAsync"/>); if it cannot be yet, this returns
        /// false and the race must not start, so the earlier one is never discarded. Otherwise this race becomes
        /// the save's pending race (saved) and the one in flight.
        /// </summary>
        /// <param name="messages">Receives what the player should be told (the earlier race's outcome, or why this one waits)</param>
        Task<bool> BeginRaceAsync(RaceContext context, List<PlayerMessage> messages);

        /// <summary>AC was started for the race: from now on a race without a result is a race walked away from. Saves.</summary>
        void MarkRaceLaunched(RaceContext context);

        /// <summary>The race never ran (the launch failed or was stopped before AC started): it is no longer pending. Saves.</summary>
        Task ReleasePendingRaceAsync(RaceContext context);

        /// <summary>The launch of this race is over, whatever came of it; the orphan pass may look at the inbox again</summary>
        void EndRace(RaceContext context);

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
        /// Result files that are there but could not be dealt with now (locked or unreadable, the dedup or settled
        /// check failing, the archive move or the save failing). They stay in the inbox for the next pass; a race
        /// with one of these is never taken for a race without a result.
        /// </summary>
        public int FilesDeferred { get; set; }

        /// <summary>
        /// Of <see cref="FilesQuarantined"/>, those that belonged to the race in hand (its context id, or written
        /// after it was set up when the file says nothing). Other files quarantined on the way (another save's,
        /// a late one) say nothing about this race.
        /// </summary>
        public int FilesQuarantinedForContext { get; set; }

        /// <summary>The inbox itself could not be read: a result may be in it</summary>
        public bool InboxUnreadable { get; set; }

        /// <summary>Something that may be this race's result is waiting to be tried again</summary>
        public bool RetryLater => FilesDeferred > 0 || InboxUnreadable;

        /// <summary>The orphan pass left the pending race for later: Assetto Corsa is (or may be) still running</summary>
        public bool WaitingForAssettoCorsa { get; set; }

        /// <summary>The orphan pass settled the pending race as forfeited (it ran and brought back nothing)</summary>
        public bool ForfeitApplied { get; set; }

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
