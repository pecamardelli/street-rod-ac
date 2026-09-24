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
    /// a catalog.db that cannot be opened is put aside as catalog.db.bad and a new one made, rather than every
    /// call failing for good.
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
            _migrated = true;

            if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(DefaultPath), StringComparison.OrdinalIgnoreCase)) return;
            if (!File.Exists(LegacyPath)) return;

            try
            {
                if (File.Exists(path))
                {
                    // Both there: the new one is the one in use; the old one only confuses the Load screen
                    File.Move(LegacyPath, LegacyPath + ".old", overwrite: true);
                    Logger.Warning("An old catalog.db was left among the saves; renamed it to {Path}", LegacyPath + ".old");
                    return;
                }

                File.Move(LegacyPath, path);
                foreach (var side in new[] { "-log", "-tmp" })
                {
                    var from = Path.Combine(Path.GetDirectoryName(LegacyPath)!, "catalog" + side + ".db");
                    if (File.Exists(from)) File.Move(from, Path.Combine(Path.GetDirectoryName(path)!, "catalog" + side + ".db"), overwrite: true);
                }

                Logger.Information("Moved catalog.db from the saves folder to {Path}", path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The import rebuilds the catalog from the install; only hand-edited profiles stay behind
                Logger.Warning("Could not move catalog.db from the saves folder ({Error}); starting a new catalog", ex.Message);
            }
        }

        private static LiteDatabase OpenOrRecreate(string path)
        {
            try
            {
                return new Connection(path);
            }
            catch (Exception ex) when (ex is LiteException or InvalidCastException or InvalidDataException or FormatException)
            {
                // Not "in use" (that is an IOException and goes to the caller): the file itself is bad
                var bad = path + ".bad";
                Logger.Warning(ex, "catalog.db cannot be read; moving it to {Bad} and starting a new one. " +
                    "Car profiles edited by hand are in the old file", bad);

                File.Move(path, bad, overwrite: true);
                var log = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "-log.db");
                if (File.Exists(log)) File.Move(log, log + ".bad", overwrite: true);

                return new Connection(path);
            }
        }

        private sealed class Connection : LiteDatabase
        {
            private readonly bool _opened;
            private int _released;

            public Connection(string path) : base(path)
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
