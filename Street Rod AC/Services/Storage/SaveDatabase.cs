using LiteDB;
using Street_Rod_AC.Helpers;
using Street_Rod_AC.Logging;
using System.IO;

namespace Street_Rod_AC.Services.Storage
{
    /// <summary>
    /// The one open LiteDB of the save in use. LiteDB's direct mode wants a single long-lived instance per
    /// file: a second open of the same file throws, and every open and close pays a full checkpoint. The game
    /// state and the race sessions of a save both go through here, so they never open the file twice and can
    /// share a transaction. Opening another save closes the one before it; the Load screen lists the saves
    /// through <see cref="Peek{T}"/>, which never switches the open save.
    ///
    /// Every use holds a lock for its whole duration: the UI thread saves while the race pipeline reads on a
    /// worker, and neither may see the instance switched or closed under it.
    /// </summary>
    public sealed class SaveDatabase : IDisposable
    {
        /// <summary>The content catalog's database; it used to live among the saves and may still be there</summary>
        public const string ReservedCatalogName = "catalog";

        /// <summary>Suffixes of LiteDB's side files next to a save: the journal and the rebuild copy</summary>
        private static readonly string[] SideFileSuffixes = { "-log", "-tmp" };

        private readonly object _gate = new();

        // The save format's mapper (see SaveMapper), this instance's own: it is only ever used under the lock
        private readonly BsonMapper _mapper = SaveMapper.Create();
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger(LogCategory.Save);
        private LiteDatabase? _database;
        private string? _openSave;
        private bool _disposed;

        public SaveDatabase() : this(DefaultSavesDirectory)
        {
        }

        public SaveDatabase(string savesDirectory)
        {
            SavesDirectory = savesDirectory;
            Directory.CreateDirectory(SavesDirectory);
        }

        /// <summary>%AppData%\StreetRodAC\Saves</summary>
        public static string DefaultSavesDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StreetRodAC", "Saves");

        public string SavesDirectory { get; }

        /// <summary>
        /// True when the name can be a save: one clean path segment, not the catalog's name, and not the name
        /// of one of LiteDB's side files (a "Bob-log" save would be taken for the journal of "Bob").
        /// </summary>
        public static bool IsValidSaveName(string? saveName) =>
            PathNames.IsSafeSegment(saveName)
            && !saveName!.Equals(ReservedCatalogName, StringComparison.OrdinalIgnoreCase)
            && !SideFileSuffixes.Any(s => saveName.EndsWith(s, StringComparison.OrdinalIgnoreCase));

        /// <summary>The save's database file; throws on a name that is not a valid save name</summary>
        public string PathOf(string saveName)
        {
            if (!IsValidSaveName(saveName))
                throw new ArgumentException($"'{saveName}' is not a valid save name", nameof(saveName));
            return Path.Combine(SavesDirectory, saveName + ".db");
        }

        /// <summary>The save's database and every file LiteDB may have left next to it</summary>
        public IEnumerable<string> FilesOf(string saveName)
        {
            var path = PathOf(saveName);
            yield return path;
            foreach (var suffix in SideFileSuffixes)
                yield return Path.Combine(SavesDirectory, saveName + suffix + ".db");
        }

        /// <summary>Runs <paramref name="work"/> on the save's open database, opening it (and creating the file) if needed</summary>
        public T Use<T>(string saveName, Func<LiteDatabase, T> work)
        {
            lock (_gate)
            {
                return work(Open(saveName));
            }
        }

        /// <inheritdoc cref="Use{T}"/>
        public void Use(string saveName, Action<LiteDatabase> work) => Use(saveName, db => { work(db); return true; });

        /// <summary>
        /// Runs a read on a save without making it the open one, so listing the saves never closes the save in
        /// use. The save in use is read through its open instance; any other save through a short read-only
        /// instance that is disposed before the lock is released. Read-only opens neither write nor checkpoint:
        /// a journal left by a crash is read and left where it is. Holding the lock keeps this app from writing
        /// the file while it is read. Throws when the file does not exist.
        /// </summary>
        public T Peek<T>(string saveName, Func<LiteDatabase, T> read)
        {
            var path = PathOf(saveName);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_database != null && IsOpen(saveName))
                    return read(_database);

                if (!File.Exists(path))
                    throw new FileNotFoundException($"Save '{saveName}' does not exist", path);

                using var readOnly = new LiteDatabase(new ConnectionString
                {
                    Filename = path,
                    ReadOnly = true,
                    Connection = ConnectionType.Direct
                }, _mapper);
                return read(readOnly);
            }
        }

        /// <summary>
        /// Runs <paramref name="work"/> in one LiteDB transaction: all of it is written, or none of it. LiteDB's
        /// transactions belong to the thread, and the lock keeps the whole of it on this one.
        /// </summary>
        public void InTransaction(string saveName, Action<LiteDatabase> work)
        {
            lock (_gate)
            {
                var db = Open(saveName);
                if (!db.BeginTrans())
                    throw new InvalidOperationException($"A transaction is already open on save '{saveName}'");
                try
                {
                    work(db);
                    db.Commit();
                }
                catch
                {
                    try
                    {
                        db.Rollback();
                    }
                    catch (Exception rollbackError)
                    {
                        // LiteDB's transactions belong to the thread: one left open here would make every later
                        // BeginTrans on this thread fail, and no save would succeed for the rest of the session.
                        // Closing the instance drops it; the next use opens the file again cleanly. The error the
                        // caller hears about stays the one that made the transaction fail.
                        _logger.Error(rollbackError, "Rolling back a failed write to save {SaveName} failed too: the database is closed and reopened on next use", saveName);
                        CloseOpen();
                    }

                    throw;
                }
            }
        }

        /// <summary>
        /// Copies what the save holds to <paramref name="destination"/> (a backup). The open instance first
        /// writes its journal into the data file (checkpoint), and the lock keeps anything from writing while
        /// the file is copied. LiteDB opens its data file for writing with FileShare.Read, so the copy reads it
        /// through a handle that shares read and write.
        /// </summary>
        public void CopyTo(string saveName, string destination)
        {
            var path = PathOf(saveName);
            lock (_gate)
            {
                if (_database != null && IsOpen(saveName))
                    _database.Checkpoint();

                using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
                source.CopyTo(target);
            }
        }

        /// <summary>Closes the save's database if it is the open one (before its files are deleted)</summary>
        public void Close(string saveName)
        {
            lock (_gate)
            {
                if (IsOpen(saveName)) CloseOpen();
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                DisposeUnderLock();
            }
        }

        /// <summary>
        /// Dispose for the crash path: the thread that holds the lock may be the one that is stuck (a save on a UI
        /// thread that no longer answers), so the lock is waited for only so long. False when it was not free in
        /// time; the database is then left to the process's end (LiteDB's journal makes that safe).
        /// </summary>
        public bool TryDispose(TimeSpan wait)
        {
            if (!Monitor.TryEnter(_gate, wait)) return false;
            try
            {
                DisposeUnderLock();
                return true;
            }
            finally
            {
                Monitor.Exit(_gate);
            }
        }

        private void DisposeUnderLock()
        {
            if (_disposed) return;
            _disposed = true;
            CloseOpen();
        }

        private bool IsOpen(string saveName) =>
            _openSave != null && _openSave.Equals(saveName, StringComparison.OrdinalIgnoreCase);

        // Called under the lock
        private LiteDatabase Open(string saveName)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var path = PathOf(saveName);

            if (_database != null && IsOpen(saveName))
                return _database;

            CloseOpen();
            _database = new LiteDatabase(path, _mapper);
            _openSave = saveName;
            _logger.Debug("Opened save database {SaveName}", saveName);
            return _database;
        }

        // Called under the lock. Disposing checkpoints the journal into the data file and deletes it.
        private void CloseOpen()
        {
            if (_database == null) return;
            try
            {
                _database.Dispose();
                _logger.Debug("Closed save database {SaveName}", _openSave);
            }
            catch (Exception ex)
            {
                _logger.Warning("Save database {SaveName} did not close cleanly: {Error}", _openSave, ex.Message);
            }
            finally
            {
                _database = null;
                _openSave = null;
            }
        }
    }
}
