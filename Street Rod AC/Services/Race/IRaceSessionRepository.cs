using Street_Rod_AC.Models.Race;

namespace Street_Rod_AC.Services.Race
{
    /// <summary>
    /// Repository for persisting and querying processed race sessions
    /// Stores sessions in LiteDB for deduplication and historical tracking
    /// </summary>
    public interface IRaceSessionRepository
    {
        /// <summary>
        /// Save a processed race session to the database
        /// </summary>
        /// <param name="session">Session to save</param>
        Task SaveAsync(ProcessedRaceSession session);

        /// <summary>
        /// Get a processed session by its session ID
        /// </summary>
        /// <param name="sessionId">Session UUID</param>
        /// <returns>Session if found, null otherwise</returns>
        Task<ProcessedRaceSession?> GetBySessionIdAsync(string sessionId);

        /// <summary>
        /// Check if a session ID has already been processed
        /// </summary>
        /// <param name="sessionId">Session UUID to check</param>
        /// <returns>True if processed, false if new</returns>
        Task<bool> IsProcessedAsync(string sessionId);

        /// <summary>
        /// Get all processed sessions for the current save
        /// </summary>
        /// <returns>List of all sessions</returns>
        Task<List<ProcessedRaceSession>> GetAllAsync();

        /// <summary>
        /// Get the count of processed sessions
        /// </summary>
        /// <returns>Total session count</returns>
        Task<int> GetCountAsync();
    }
}
