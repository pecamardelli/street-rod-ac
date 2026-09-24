using LiteDB;
using Street_Rod_AC.Models.Race;

namespace Street_Rod_AC.Services.Race
{
    /// <summary>
    /// Repository for persisting and querying processed race sessions
    /// Stores sessions in LiteDB for deduplication and historical tracking, in the save's own database.
    /// The save is named by the caller, which holds the game state; the repository never looks it up.
    /// </summary>
    public interface IRaceSessionRepository
    {
        /// <summary>
        /// Save a processed race session to the database
        /// </summary>
        Task SaveAsync(string saveName, ProcessedRaceSession session);

        /// <summary>
        /// Writes the session into a database a transaction is already open on (the game state's save), so
        /// the record and the state it changed are committed together
        /// </summary>
        void Save(LiteDatabase database, ProcessedRaceSession session);

        /// <summary>
        /// Get a processed session by its session ID
        /// </summary>
        /// <returns>Session if found, null otherwise</returns>
        Task<ProcessedRaceSession?> GetBySessionIdAsync(string saveName, string sessionId);

        /// <summary>
        /// Check if a session ID has already been processed. Throws when the save cannot be read: a result
        /// must not be applied twice because a read failed.
        /// </summary>
        Task<bool> IsProcessedAsync(string saveName, string sessionId);

        /// <summary>
        /// True when a result or a forfeit has already been recorded for this race context: a second file for
        /// the same race (another session id) must not settle it again. Throws when the save cannot be read.
        /// </summary>
        Task<bool> IsContextSettledAsync(string saveName, Guid contextId);

        /// <summary>
        /// Get all processed sessions of the save
        /// </summary>
        Task<List<ProcessedRaceSession>> GetAllAsync(string saveName);

        /// <summary>
        /// Get the count of processed sessions
        /// </summary>
        Task<int> GetCountAsync(string saveName);
    }
}
