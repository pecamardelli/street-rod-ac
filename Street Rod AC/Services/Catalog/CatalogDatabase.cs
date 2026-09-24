using System.IO;
using LiteDB;
using Street_Rod_AC.Logging;

namespace Street_Rod_AC.Services.Catalog
{
    /// <summary>
    /// The one door to catalog.db. The repositories open the file for every call, and LiteDB opens it
    /// exclusively: two threads asking at the same moment (the parts warm-up in the background, a screen on the
    /// UI thread) would find it locked. So calls take turns.
    /// A call must not open the database again while it has it open: the second one would wait for the first.
    /// A thread that tries is stopped with an exception rather than left hanging, and a turn that does not come
    /// within <see cref="TurnTimeout"/> is an error, not a frozen window.
    ///
    /// The catalog lives in %AppData%\StreetRodAC\Catalog, apart from the saves: the Load screen lists every
    /// database in the saves folder, and a player called "Catalog" would otherwise have overwritten it. A
    /// catalog.db left in the saves folder by an older version is moved over once.
    ///
    /// Everything in the catalog is read from the install again at start-up, except profiles edited by hand. So
    /// a catalog.db that is corrupt is put aside as catalog.db.bad and a new one made, rather than every call
    /// failing for good. Only a file LiteDB says is broken goes: one written by LiteDB 4 is upgraded in place, and a
    /// file that is busy, locked or refused for any other reason goes to the caller untouched.
    /// </summary>
    internal static class CatalogDatabase
    {
        /// <summary>How long a caller waits for its turn before giving up with an error</summary>
        public static readonly TimeSpan TurnTimeout = TimeSpan.FromSeconds(60);

        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger(LogCategory.Catalog);

        // Not a Monitor: whoever disposes the database lets the next caller in, on whatever thread that happens
        private static readonly SemaphoreSlim Turn = new(1, 1);

        /// <summary>Managed thread id of whoever has the turn, 0 when nobody has; only to catch a nested open</summary>
        private static int _holderThread;

        private static bool _migrated;

        private static string AppDataFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StreetRodAC");

        /// <summary>%AppData%\StreetRodAC\Catalog\catalog.db, the path every repository opens</summary>
        public static string DefaultPath => Path.Combine(AppDataFolder, "Catalog", "catalog.db");

        /// <summary>Where catalog.db lived before: among the saves</summary>
        private static string LegacyPath => Path.Combine(AppDataFolder, "Saves", "catalog.db");

        /// <summary>Opens the database once nobody else has it open; disposing it lets the next caller in</summary>
        public static LiteDatabase Open(string path)
        {
            var thread = Environment.CurrentManagedThreadId;
            if (Volatile.Read(ref _holderThread) == thread)
            {
                // Waiting here would wait for ourselves, for good
                var nested = new InvalidOperationException("catalog.db opened again by the thread that has it open");
                Logger.Error(nested, "Nested catalog open; the call is refused instead of hanging");
                throw nested;
            }

            if (!Turn.Wait(TurnTimeout))
            {
                var timeout = new TimeoutException($"catalog.db stayed busy for {TurnTimeout.TotalSeconds:0} s");
                Logger.Error(timeout, "Gave up waiting for catalog.db");
                throw timeout;
            }

            Volatile.Write(ref _holderThread, thread);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                MigrateOnce(path);
                return OpenOrRecreate(path);
            }
            catch
            {
                Volatile.Write(ref _holderThread, 0);
                Turn.Release();
                throw;
            }
        }

        /// <summary>
        /// Moves a catalog.db from the saves folder (with LiteDB's side files) to the catalog folder, the first
        /// time the catalog is opened. Runs with the turn held, so no repository has either file open.
        /// </summary>
        private static void MigrateOnce(string path)
        {
            if (_migrated) return;

            // Only the default catalog has an old home to come from. Another path (a test's) says nothing about it,
            // and must not stop the default one from being moved when it is opened later.
            if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(DefaultPath), StringComparison.OrdinalIgnoreCase)) return;

            // Handled once per run, whatever the outcome: a move that failed has left the old catalog whole where it
            // was, and trying again on every open would only log the same failure
            _migrated = true;

            if (!File.Exists(LegacyPath)) return;

            if (File.Exists(path))
            {
                try
                {
                    // Both there: the new one is the one in use; the old one only confuses the Load screen
                    File.Move(LegacyPath, LegacyPath + ".old", overwrite: true);
                    Logger.Warning("An old catalog.db was left among the saves; renamed it to {Path}", LegacyPath + ".old");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Logger.Warning("Could not rename the old catalog.db among the saves ({Error}); it stays at {Path}", ex.Message, LegacyPath);
                }

                return;
            }

            // The side files first, the data file last. catalog-log.db holds the writes not yet folded into the
            // data file: moved after it, a failure would leave them behind, lost to a catalog that went ahead
            // without them. Moved first, a failure puts them back and the old catalog stays whole where it was.
            var legacyFolder = Path.GetDirectoryName(LegacyPath)!;
            var folder = Path.GetDirectoryName(path)!;
            var moved = new List<(string From, string To)>();
            try
            {
                foreach (var side in new[] { "-log", "-tmp" })
                {
                    var from = Path.Combine(legacyFolder, "catalog" + side + ".db");
                    var to = Path.Combine(folder, "catalog" + side + ".db");
                    if (!File.Exists(from)) continue;

                    File.Move(from, to, overwrite: true);
                    moved.Add((from, to));
                }

                File.Move(LegacyPath, path);
                Logger.Information("Moved catalog.db from the saves folder to {Path}", path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                var restored = true;
                foreach (var (from, to) in Enumerable.Reverse(moved))
                {
                    try
                    {
                        File.Move(to, from, overwrite: true);
                    }
                    catch (Exception back) when (back is IOException or UnauthorizedAccessException)
                    {
                        restored = false;
                        Logger.Error(back, "Could not put {File} back among the saves; it is at {Path}", Path.GetFileName(from), to);
                    }
                }

                // The import rebuilds the catalog from the install; only hand-edited profiles stay behind, in the old file
                if (restored)
                    Logger.Warning("Could not move catalog.db from the saves folder ({Error}); it stays at {Old}, and a new " +
                        "catalog is started at {Path}", ex.Message, LegacyPath, path);
                else
                    Logger.Error(ex, "Could not move catalog.db from the saves folder, and its side files are split between " +
                        "{Old} and {Folder}; a new catalog is started", legacyFolder, folder);
            }
        }

        private static LiteDatabase OpenOrRecreate(string path)
        {
            try
            {
                return new Connection(path);
            }
            catch (LiteException ex) when (ex.ErrorCode == LiteException.INVALID_DATABASE)
            {
                // "Not a LiteDB 5 file": either written by LiteDB 4, which LiteDB upgrades in place (keeping a backup
                // of the old file next to it), or not a database at all. Only once the upgrade fails is it broken.
                try
                {
                    var upgraded = new Connection(new ConnectionString { Filename = path, Upgrade = true });
                    Logger.Warning("catalog.db was written by an older LiteDB; upgraded it in place");
                    return upgraded;
                }
                catch (Exception upgrade) when (IsCorrupt(upgrade))
                {
                    return Recreate(path, upgrade);
                }
            }
            catch (Exception ex) when (IsCorrupt(ex))
            {
                return Recreate(path, ex);
            }
        }

        /// <summary>
        /// The errors that say the file itself is broken. Not "in use" (an IOException, or LiteDB's lock timeout
        /// and already-open), not a password (the catalog has none; a file that wants one is somebody else's to
        /// look at), not a database shut down under us: those go to the caller and the file stays.
        /// </summary>
        private static bool IsCorrupt(Exception ex) => ex switch
        {
            LiteException lite => lite.ErrorCode is LiteException.INVALID_DATABASE
                or LiteException.INVALID_DATAFILE_STATE
                or LiteException.INVALID_FREE_SPACE_PAGE,
            // Thrown while decoding the pages of the file; opening reads nothing else
            InvalidCastException or InvalidDataException or FormatException or EndOfStreamException => true,
            _ => false
        };

        private static LiteDatabase Recreate(string path, Exception ex)
        {
            var bad = path + ".bad";
            Logger.Warning(ex, "catalog.db is corrupt ({Type} {Code}); moving it to {Bad} and starting a new one. " +
                "Car profiles edited by hand are in the old file", ex.GetType().Name,
                ex is LiteException lite ? lite.ErrorCode : 0, bad);

            File.Move(path, bad, overwrite: true);
            foreach (var side in new[] { "-log", "-tmp" })
            {
                var file = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + side + ".db");
                if (File.Exists(file)) File.Move(file, file + ".bad", overwrite: true);
            }

            return new Connection(path);
        }

        private sealed class Connection : LiteDatabase
        {
            private readonly bool _opened;
            private int _released;

            public Connection(string path) : this(new ConnectionString(path))
            {
            }

            public Connection(ConnectionString connection) : base(connection)
            {
                // A file that could not be opened gives its turn back in Open, not here
                _opened = true;
            }

            protected override void Dispose(bool disposing)
            {
                try
                {
                    base.Dispose(disposing);
                }
                finally
                {
                    // Also when closing the file failed, and from the finalizer of a connection nobody disposed:
                    // a turn that is never given back would stop every later call for good
                    if (_opened && Interlocked.Exchange(ref _released, 1) == 0)
                    {
                        Volatile.Write(ref _holderThread, 0);
                        Turn.Release();
                    }
                }
            }
        }
    }
}
