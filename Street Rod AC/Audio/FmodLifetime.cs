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

    /// <summary>
    /// Releases both systems: what plays, then the banks, then the system. Only the first call does anything, and it
    /// never throws: an exit must go ahead whatever FMOD makes of it.
    /// </summary>
    public static void Shutdown()
    {
        if (Interlocked.Exchange(ref _shutDown, 1) == 1) return;

        try
        {
            EngineAudio.ShutdownShared();
        }
        catch
        {
            // Nothing is left to be done about it on the way out
        }

        try
        {
            EngineLoudness.Shutdown();
        }
        catch
        {
            // Nor about this one
        }
    }
}
