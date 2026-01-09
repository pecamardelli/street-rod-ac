using LiteDB;
using Street_Rod_AC.Models.GameState;
using System.IO;

namespace Street_Rod_AC.Services.Storage
{
    public class GameStateRepository : IGameStateRepository
    {
        private readonly string _savesDirectory;

        public GameStateRepository()
        {
            // Saves directory in AppData
            _savesDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "StreetRodAC",
                "Saves");

            // Ensure directory exists
            Directory.CreateDirectory(_savesDirectory);
        }

        public GameState? Load(string saveName)
        {
            var dbPath = GetDatabasePath(saveName);

            if (!File.Exists(dbPath))
                return null;

            try
            {
                using var db = new LiteDatabase(dbPath);
                var collection = db.GetCollection<GameState>("gamestate");
                var state = collection.FindById(1);

                if (state != null)
                {
                    state.LastPlayedDate = DateTime.Now;
                    state.SaveName = saveName;
                }

                return state;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load save '{saveName}': {ex.Message}", ex);
            }
        }

        public void Save(GameState state, string saveName)
        {
            var dbPath = GetDatabasePath(saveName);

            try
            {
                // Create backup before saving
                CreateBackup(dbPath);

                using var db = new LiteDatabase(dbPath);
                var collection = db.GetCollection<GameState>("gamestate");

                state.LastPlayedDate = DateTime.Now;
                collection.Upsert(state);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save '{saveName}': {ex.Message}", ex);
            }
        }

        public bool Exists(string saveName)
        {
            var dbPath = GetDatabasePath(saveName);
            return File.Exists(dbPath);
        }

        public void Delete(string saveName)
        {
            var dbPath = GetDatabasePath(saveName);

            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);

                // Also delete backups
                for (int i = 1; i <= 3; i++)
                {
                    var backupPath = $"{dbPath}.backup{i}";
                    if (File.Exists(backupPath))
                        File.Delete(backupPath);
                }
            }
        }

        public List<string> ListSaves()
        {
            if (!Directory.Exists(_savesDirectory))
                return [];

            var saves = Directory.GetFiles(_savesDirectory, "*.db")
                .Where(f => !f.Contains(".backup"))
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .OrderByDescending(f => File.GetLastWriteTime(Path.Combine(_savesDirectory, f + ".db")))
                .ToList();

            return saves;
        }

        public GameState CreateNew(string saveName, string playerName)
        {
            var state = GameState.CreateNew(playerName);
            state.SaveName = saveName;
            Save(state, saveName);
            return state;
        }

        private string GetDatabasePath(string saveName)
        {
            return Path.Combine(_savesDirectory, $"{saveName}.db");
        }

        private void CreateBackup(string dbPath)
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

                File.Copy(dbPath, backup1, true);
            }
            catch
            {
                // Backup failure shouldn't prevent saving
                // Log error in production
            }
        }
    }
}
