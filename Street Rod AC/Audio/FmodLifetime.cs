namespace Street_Rod_AC.Audio;

/// <summary>
/// The end of FMOD in this process. Two Studio systems may be up: the one the garage plays through, and the silent
/// one engine banks are measured on. Left running, their mixer and Studio threads (and the DSP callbacks into this
/// assembly) go on while the runtime tears down, and exit can crash or hang. Called on the way out, the normal one
/// and the fatal one.
/// </summary>
public static class FmodLifetime
{
    private static int _shutDown;

    // How long the exit waits, all told, for FMOD work in progress to let go
    private static readonly TimeSpan WaitAtMost = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// Releases both systems: what plays, then the banks, then the system. Only the first call does anything, and it
    /// never throws: an exit must go ahead whatever FMOD makes of it. Both share one deadline, so a load or a
    /// measurement in progress holds the exit up by <see cref="WaitAtMost"/> at most; one still inside FMOD by then
    /// keeps its system (see <see cref="EngineAudio"/>).
    /// </summary>
    public static void Shutdown()
    {
        if (Interlocked.Exchange(ref _shutDown, 1) == 1) return;

        var deadline = DateTime.UtcNow + WaitAtMost;

        // A garage load measures its bank inside the garage's gate: the measurement hears of the exit first, so
        // both can be let go of within the one wait
        try
        {
            EngineLoudness.BeginShutdown();
        }
        catch
        {
            // Only a flag
        }

        try
        {
            EngineAudio.ShutdownShared(deadline);
        }
        catch
        {
            // Nothing is left to be done about it on the way out
        }

        try
        {
            EngineLoudness.Shutdown(deadline);
        }
        catch
        {
            // Nor about this one
        }
    }
}
