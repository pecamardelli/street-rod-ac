using LiteDB;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Race;
using System.IO;

namespace Street_Rod_AC.Services.Race
{
    /// <summary>
    /// Repository for managing processed race sessions in LiteDB
    /// Stores sessions in the current save's database file
    /// Collection: "ProcessedRaceSessions"
    /// </summary>
    public class RaceSessionRepository : IRaceSessionRepository
    {
        private readonly string _savesDirectory;
        private readonly IAppLogger _logger;

        public RaceSessionRepository()
        {
            // Same saves directory as GameStateRepository
            _savesDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "StreetRodAC",
                "Saves");

            Directory.CreateDirectory(_savesDirectory);

            _logger = AppLoggerFactory.CreateLogger(LogCategory.RaceIngestion);
        }

        public async Task SaveAsync(ProcessedRaceSession session)
        {
            await Task.Run(() =>
            {
                var dbPath = GetDatabasePath();

                if (string.IsNullOrEmpty(dbPath))
                {
                    _logger.Warning("Cannot save session: no active game state");
                    throw new InvalidOperationException("No active game state - cannot save session");
                }

                try
                {
                    using var db = new LiteDatabase(dbPath);
                    var collection = db.GetCollection<ProcessedRaceSession>("ProcessedRaceSessions");

                    // Ensure session_id index exists
                    collection.EnsureIndex(x => x.SessionId, unique: true);

                    // Upsert (insert or update)
                    collection.Upsert(session);

                    _logger.Debug("Saved processed session {SessionId} to database", session.SessionId);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to save session {SessionId}", session.SessionId);
                    throw new InvalidOperationException($"Failed to save session: {ex.Message}", ex);
                }
            });
        }

        public async Task<ProcessedRaceSession?> GetBySessionIdAsync(string sessionId)
        {
            return await Task.Run(() =>
            {
                var dbPath = GetDatabasePath();

                if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
                    return null;

                try
                {
                    using var db = new LiteDatabase(dbPath);
                    var collection = db.GetCollection<ProcessedRaceSession>("ProcessedRaceSessions");
                    return collection.FindById(sessionId);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error querying session {SessionId}", sessionId);
                    return null;
                }
            });
        }

        public async Task<bool> IsProcessedAsync(string sessionId)
        {
            var session = await GetBySessionIdAsync(sessionId);
            return session != null;
        }

        public async Task<List<ProcessedRaceSession>> GetAllAsync()
        {
            return await Task.Run(() =>
            {
                var dbPath = GetDatabasePath();

                if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
                    return new List<ProcessedRaceSession>();

                try
                {
                    using var db = new LiteDatabase(dbPath);
                    var collection = db.GetCollection<ProcessedRaceSession>("ProcessedRaceSessions");
                    return collection.FindAll().ToList();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error retrieving all sessions");
                    return new List<ProcessedRaceSession>();
                }
            });
        }

        public async Task<int> GetCountAsync()
        {
            return await Task.Run(() =>
            {
                var dbPath = GetDatabasePath();

                if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
                    return 0;

                try
                {
                    using var db = new LiteDatabase(dbPath);
                    var collection = db.GetCollection<ProcessedRaceSession>("ProcessedRaceSessions");
                    return collection.Count();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error getting session count");
                    return 0;
                }
            });
        }

        /// <summary>
        /// Get the database path for the current save
        /// </summary>
        private string? GetDatabasePath()
        {
            // Get current game state from application
            var app = System.Windows.Application.Current as App;
            var currentGameState = app?.CurrentGameState;

            if (currentGameState == null || string.IsNullOrEmpty(currentGameState.SaveName))
            {
                _logger.Warning("No active game state - cannot determine database path");
                return null;
            }

            var dbPath = Path.Combine(_savesDirectory, $"{currentGameState.SaveName}.db");
            return dbPath;
        }
    }
}
