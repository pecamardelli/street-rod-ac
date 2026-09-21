using LiteDB;

namespace Street_Rod_AC.Services.Catalog
{
    /// <summary>
    /// The one door to catalog.db. The repositories open the file for every call, and LiteDB opens it
    /// exclusively: two threads asking at the same moment (the parts warm-up in the background, a screen on the
    /// UI thread) would find it locked. So calls take turns.
    /// A call must not open the database again while it has it open: the second one would wait for the first.
    /// </summary>
    internal static class CatalogDatabase
    {
        // Not a Monitor: whoever disposes the database lets the next caller in, on whatever thread that happens
        private static readonly SemaphoreSlim Turn = new(1, 1);

        /// <summary>Opens the database once nobody else has it open; disposing it lets the next caller in</summary>
        public static LiteDatabase Open(string path)
        {
            Turn.Wait();
            try
            {
                return new Connection(path);
            }
            catch
            {
                Turn.Release();
                throw;
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
                    if (_opened && Interlocked.Exchange(ref _released, 1) == 0) Turn.Release();
                }
            }
        }
    }
}
