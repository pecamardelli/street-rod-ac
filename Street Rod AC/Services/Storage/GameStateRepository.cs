using LiteDB;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Services.Opponents;
using System.IO;

namespace Street_Rod_AC.Services.Storage
{
    public class GameStateRepository : IGameStateRepository
    {
        private const string Collection = "gamestate";
        private const int BackupCount = 3;

        private readonly IOpponentInitializationService? _opponentInitializationService;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger(LogCategory.Save);

        /// <param name="database">
        /// The saves' database owner to share with the race session repository; null makes one of its own.
        /// Either way this repository disposes it.
        /// </param>
        public GameStateRepository(IOpponentInitializationService? opponentInitializationService = null, SaveDatabase? database = null)
        {
            _opponentInitializationService = opponentInitializationService;
            Database = database ?? new SaveDatabase();
        }

        /// <summary>The open database of the save in use, shared with the race session repository</summary>
        public SaveDatabase Database { get; }

        public GameState? Load(string saveName)
        {
            var dbPath = Database.PathOf(saveName);

            if (!File.Exists(dbPath))
                return null;

            try
            {
                var state = Database.Use(saveName, db => db.GetCollection<GameState>(Collection).FindById(1));

                // LastPlayedDate is left as it was saved: the Load screen loads every save to list it, and
                // stamping it here made them all "just now". Save stamps it.
                if (state != null)
                    state.SaveName = saveName;

                return state;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load save '{saveName}': {ex.Message}", ex);
            }
        }

        public void Save(GameState state, string saveName) => Save(state, saveName, null);

        public void Save(GameState state, string saveName, Action<LiteDatabase>? sameTransaction)
        {
            var dbPath = Database.PathOf(saveName);

            try
            {
                // Create backup before saving
                CreateBackup(saveName, dbPath);

                state.LastPlayedDate = DateTime.Now;
                Database.InTransaction(saveName, db =>
                {
                    db.GetCollection<GameState>(Collection).Upsert(state);
                    sameTransaction?.Invoke(db);
                });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save '{saveName}': {ex.Message}", ex);
            }
        }

        public bool Exists(string saveName) =>
            SaveDatabase.IsValidSaveName(saveName) && File.Exists(Database.PathOf(saveName));

        public void Delete(string saveName)
        {
            var dbPath = Database.PathOf(saveName);
            if (!File.Exists(dbPath))
                return;

            Database.Close(saveName);

            // The save, LiteDB's journal and rebuild files next to it, and its backups
            foreach (var file in Database.FilesOf(saveName))
            {
                if (File.Exists(file))
                    File.Delete(file);
            }

            for (int i = 1; i <= BackupCount; i++)
            {
                var backupPath = $"{dbPath}.backup{i}";
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
            }
        }

        public List<string> ListSaves()
        {
            if (!Directory.Exists(Database.SavesDirectory))
                return [];

            // Only real saves: not the catalog, not LiteDB's "-log"/"-tmp" side files, not backups
            return Directory.GetFiles(Database.SavesDirectory, "*.db")
                .Where(f => !f.Contains(".backup"))
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .Where(SaveDatabase.IsValidSaveName)
                .OrderByDescending(f => File.GetLastWriteTime(Path.Combine(Database.SavesDirectory, f + ".db")))
                .ToList();
        }

        public GameState CreateNew(string saveName, string playerName)
        {
            // Throws on a name that cannot be a save before anything is made
            Database.PathOf(saveName);

            var state = GameState.CreateNew(playerName);
            state.SaveName = saveName;

            // Initialize opponents if service is available
            _opponentInitializationService?.InitializeOpponents(state);

            Save(state, saveName);
            return state;
        }

        /// <summary>Closes the open save; call it after the last save on exit</summary>
        public void Dispose() => Database.Dispose();

        private void CreateBackup(string saveName, string dbPath)
        {
            if (!File.Exists(dbPath))
                return;

            try
            {
                // Rotate backups: backup3 <- backup2 <- backup1 <- current
                var backup3 = $"{dbPath}.backup3";
                var backup2 = $"{dbPath}.backup2";
                var backup1 = $"{dbPath}.backup1";

                if (File.Exists(backup2))
                    File.Copy(backup2, backup3, true);

                if (File.Exists(backup1))
                    File.Copy(backup1, backup2, true);

                // The save may be open: copied through its owner, checkpointed and with no write in progress
                Database.CopyTo(saveName, backup1);
            }
            catch (Exception ex)
            {
                // Backup failure shouldn't prevent saving
                _logger.Warning(ex, "Could not back up save {SaveName} before saving", saveName);
            }
        }
    }
}
