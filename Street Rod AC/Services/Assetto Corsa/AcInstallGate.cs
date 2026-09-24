namespace Street_Rod_AC.Services
{
    /// <summary>
    /// One lock over every change to the AC install: the cfg INI writes, the cars' data and copies going in, and
    /// all of it going back. The launcher holds it over each synchronous stretch of its prepare phase (never across
    /// an await), so a restore from another thread (the crash path runs on whichever thread crashed) can never
    /// fall between a manifest being written and the change it describes, which would leave a car or race.ini
    /// changed with nothing on record to put it back. Monitor-based, so the thread that holds it can re-enter.
    /// </summary>
    public static class AcInstallGate
    {
        private static readonly object Sync = new();

        /// <summary>Holds the gate until the returned scope is disposed; waits as long as it takes</summary>
        public static Scope Hold()
        {
            Monitor.Enter(Sync);
            return new Scope(true);
        }

        /// <summary>
        /// Runs <paramref name="action"/> under the gate when it can be had within <paramref name="timeout"/>; false
        /// (and nothing run) when it cannot. For the crash and exit paths, which must never wait forever on a UI
        /// thread that may be the one that is stuck.
        /// </summary>
        public static bool TryRun(TimeSpan timeout, Action action)
        {
            if (!Monitor.TryEnter(Sync, timeout)) return false;
            try
            {
                action();
                return true;
            }
            finally
            {
                Monitor.Exit(Sync);
            }
        }

        /// <summary>The held gate; disposing it lets go</summary>
        public readonly struct Scope(bool held) : IDisposable
        {
            public void Dispose()
            {
                if (held) Monitor.Exit(Sync);
            }
        }
    }
}
