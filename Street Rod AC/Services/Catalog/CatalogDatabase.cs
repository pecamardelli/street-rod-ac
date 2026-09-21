using LiteDB;

namespace Street_Rod_AC.Services.Catalog
{
    /// <summary>
    /// The one door to catalog.db. The repositories open the file for every call, and LiteDB opens it
    /// exclusively: two threads asking at the same moment (the parts warm-up in the background, a screen on the
    /// UI thread) would find it locked. So calls take turns.
    /// </summary>
    internal static class CatalogDatabase
    {
        private static readonly object Sync = new();

        /// <summary>Opens the database once nobody else has it open; disposing it lets the next caller in</summary>
        public static LiteDatabase Open(string path)
        {
            Monitor.Enter(Sync);
            try
            {
                return new Connection(path);
            }
            catch
            {
                Monitor.Exit(Sync);
                throw;
            }
        }

        private sealed class Connection : LiteDatabase
        {
            private bool _released;

            public Connection(string path) : base(path) { }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (_released || !disposing) return;

                _released = true;
                Monitor.Exit(Sync);
            }
        }
    }
}
