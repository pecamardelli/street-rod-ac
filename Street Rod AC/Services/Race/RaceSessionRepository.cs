using LiteDB;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Services.Storage;
using System.IO;

namespace Street_Rod_AC.Services.Race
{
    /// <summary>
    /// Repository for managing processed race sessions in LiteDB
    /// Stores sessions in the save's database file, through the same open instance as the game state
    /// (<see cref="SaveDatabase"/>): the two never open the file at once, and a race's record can be written
    /// in the same transaction as the state it changed.
    /// Collection: "ProcessedRaceSessions"
    /// </summary>
    public class RaceSessionRepository : IRaceSessionRepository
    {
        private const string Collection = "ProcessedRaceSessions";

        private readonly SaveDatabase _database;
        private readonly IAppLogger _logger;

        public RaceSessionRepository(SaveDatabase database)
        {
            _database = database;
            _logger = AppLoggerFactory.CreateLogger(LogCategory.RaceIngestion);
        }

        public async Task SaveAsync(string saveName, ProcessedRaceSession session)
        {
            await Task.Run(() =>
            {
                try
                {
                    _database.Use(saveName, db => Save(db, session));
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to save session {SessionId}", session.SessionId);
                    throw new InvalidOperationException($"Failed to save session: {ex.Message}", ex);
                }
            });
        }

        public void Save(LiteDatabase database, ProcessedRaceSession session)
        {
            // SessionId is the document's _id: the upsert is keyed on it
            database.GetCollection<ProcessedRaceSession>(Collection).Upsert(session);
            _logger.Debug("Saved processed session {SessionId} to database", session.SessionId);
        }

        public Task<ProcessedRaceSession?> GetBySessionIdAsync(string saveName, string sessionId) =>
            Task.Run(() => Read(saveName, db => db.GetCollection<ProcessedRaceSession>(Collection).FindById(sessionId)));

        public async Task<bool> IsProcessedAsync(string saveName, string sessionId) =>
            await GetBySessionIdAsync(saveName, sessionId) != null;

        public Task<bool> IsContextSettledAsync(string saveName, Guid contextId) =>
            Task.Run(() => Read(saveName, db =>
                db.GetCollection<ProcessedRaceSession>(Collection).Exists(Query.EQ(nameof(ProcessedRaceSession.RaceContextId), contextId))));

        public async Task<List<ProcessedRaceSession>> GetAllAsync(string saveName)
        {
            try
            {
                return await Task.Run(() =>
                    Read(saveName, db => db.GetCollection<ProcessedRaceSession>(Collection).FindAll().ToList()) ?? new List<ProcessedRaceSession>());
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error retrieving all sessions");
                return new List<ProcessedRaceSession>();
            }
        }

        public async Task<int> GetCountAsync(string saveName)
        {
            try
            {
                return await Task.Run(() => Read(saveName, db => db.GetCollection<ProcessedRaceSession>(Collection).Count()));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting session count");
                return 0;
            }
        }

        /// <summary>A read of a save that has no file yet finds nothing, and does not create it</summary>
        private T? Read<T>(string saveName, Func<LiteDatabase, T> read)
        {
            if (!File.Exists(_database.PathOf(saveName)))
                return default;
            return _database.Use(saveName, read);
        }
    }
}
