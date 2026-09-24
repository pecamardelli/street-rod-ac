using System.IO;
using static Street_Rod_AC.Audio.FmodStudio;

namespace Street_Rod_AC.Audio;

/// <summary>
/// How loud an engine plays, dB full scale (K-weighted), over its range: one figure per point of
/// <see cref="EngineLoudness.RpmGrid"/> × <see cref="EngineLoudness.ThrottleGrid"/>, rpm by rpm
/// </summary>
public sealed record EngineLevel(double[] Grid)
{
    /// <summary>The level at any rpm and throttle, between the points measured</summary>
    public double At(double rpm, double throttle) => EngineLoudness.Interpolate(Grid, rpm, throttle);
}

/// <summary>
/// How loud an engine bank plays, measured rather than guessed. Banks come from every corner of the modding world,
/// each mixed to its author's taste, so one car idles in a whisper where the next one roars. The engine is played
/// through a second FMOD system that mixes to nowhere, as fast as it is asked (no sound, no waiting), over a grid of
/// rpm and throttle, and a meter on the master bus takes the level as ears have it.
///
/// Banks differ in shape as much as in level: one idles in a whisper and howls at the top, the next is loud low
/// down and flat higher up. One gain per bank cannot even that out, least of all for an engine that only uses part
/// of the range (a 283 on a 4,350 limiter never hears the top of its bank). So the gain follows the grid: at every
/// rpm and throttle the engine is brought to where the middle of the library sits there (<see cref="GainDb"/>).
/// </summary>
public static class EngineLoudness
{
    /// <summary>Where the engine is listened to: rpm, and throttle at each rpm</summary>
    public static readonly double[] RpmGrid = [800, 1500, 2500, 3500, 4500, 5500, 6500, 7500];

    public static readonly double[] ThrottleGrid = [0, 0.5, 1];

    /// <summary>
    /// The level every engine is brought to at each grid point: the median of every bank in the install and the
    /// curated library (65 distinct banks, 2026-09-23), measured as here. Quiet at idle, rising to the top and with
    /// the throttle, as engines do: a bank keeps that shape, only its own dips and peaks are evened out.
    /// </summary>
    public static readonly double[] TargetGrid =
    [
        // closed  half   full
        -39.5, -34.0, -32.2, // 800
        -34.4, -31.9, -30.1, // 1500
        -32.3, -28.0, -25.1, // 2500
        -29.3, -25.5, -23.9, // 3500
        -27.2, -25.5, -22.6, // 4500
        -26.5, -25.2, -21.9, // 5500
        -25.3, -24.0, -21.2, // 6500
        -25.2, -23.1, -21.2 // 7500
    ];

    // Never more than this either way: a bank measured far off is more likely measured wrong than mixed that badly
    private const double MaxBoostDb = 20;
    private const double MaxCutDb = 12;

    /// <summary>The gain, in dB, that brings an engine measured at <paramref name="level"/> to the library's middle here</summary>
    public static double GainDb(EngineLevel level, double rpm, double throttle) =>
        Math.Clamp(Interpolate(TargetGrid, rpm, throttle) - level.At(rpm, throttle), -MaxCutDb, MaxBoostDb);

    /// <summary>Bilinear, between the grid points; held flat past either end</summary>
    public static double Interpolate(double[] grid, double rpm, double throttle)
    {
        var (r, rt) = Locate(RpmGrid, rpm);
        var (t, tt) = Locate(ThrottleGrid, throttle);
        double V(int ri, int ti) => grid[ri * ThrottleGrid.Length + ti];

        var low = V(r, t) + (V(r, t + 1) - V(r, t)) * tt;
        var high = V(r + 1, t) + (V(r + 1, t + 1) - V(r + 1, t)) * tt;
        return low + (high - low) * rt;
    }

    private static (int Index, double Fraction) Locate(double[] axis, double value)
    {
        if (value <= axis[0]) return (0, 0);
        for (var i = 0; i < axis.Length - 1; i++)
        {
            if (value <= axis[i + 1]) return (i, (value - axis[i]) / (axis[i + 1] - axis[i]));
        }

        return (axis.Length - 2, 1);
    }

    private const int SampleRate = 48000;
    private const double WarmUpSeconds = 0.2;
    private const double ListenSeconds = 0.4;

    private static readonly object Gate = new();

    private static IntPtr _system;

    /// <summary>
    /// The level of an engine; null when it makes no sound to measure. Takes a fraction of a second per bank, so it
    /// is remembered per bank (by its bytes) in the user's data. One measurement at a time.
    /// </summary>
    public static EngineLevel? Measure(string bankPath, Guid engineEvent)
    {
        lock (Gate)
        {
            var key = $"{Version}:{Parts.Sounds.SoundLibrary.ChecksumOf(bankPath)}:{engineEvent}";
            var cache = LoadCache();
            if (cache.TryGetValue(key, out var known)) return known;

            EnsureSystem();

            Check(FMOD_Studio_System_LoadBankFile(_system, Utf8(bankPath), LoadBankNormal, out var bank), "load " + Path.GetFileName(bankPath));
            try
            {
                var id = engineEvent;
                Check(FMOD_Studio_System_GetEventByID(_system, ref id, out var description), "find engine_ext");
                var maxRpm = MaxRpm(description);
                LoadSamples(description);

                var level = MeasureGrid(description, maxRpm);
                if (level == null) return null;

                cache[key] = level;
                SaveCache(cache);
                return level;
            }
            finally
            {
                FMOD_Studio_Bank_Unload(bank);
                FMOD_Studio_System_Update(_system);
            }
        }
    }

    /// <summary>
    /// Every grid point; rpm past what the bank has samples for is heard at the top of its samples. A point that
    /// makes no sound takes its neighbour's figure along the rpm; a bank silent all over is no level at all.
    /// </summary>
    private static EngineLevel? MeasureGrid(IntPtr description, float maxRpm)
    {
        var grid = new double?[RpmGrid.Length * ThrottleGrid.Length];
        for (var r = 0; r < RpmGrid.Length; r++)
        {
            for (var t = 0; t < ThrottleGrid.Length; t++)
                grid[r * ThrottleGrid.Length + t] = Listen(description, Math.Min((float)RpmGrid[r], maxRpm * 0.95f), (float)ThrottleGrid[t]);
        }

        if (grid.All(v => v == null)) return null;

        var filled = new double[grid.Length];
        for (var t = 0; t < ThrottleGrid.Length; t++)
        {
            for (var r = 0; r < RpmGrid.Length; r++)
            {
                var nearest = Enumerable.Range(0, RpmGrid.Length).OrderBy(i => Math.Abs(i - r))
                    .Select(i => grid[i * ThrottleGrid.Length + t]).FirstOrDefault(v => v != null);
                filled[r * ThrottleGrid.Length + t] = Math.Round(nearest ?? grid.First(v => v != null)!.Value, 1);
            }
        }

        return new EngineLevel(filled);
    }

    #region Cache

    // Bumped whenever the measuring changes, so old figures are not mixed with new ones
    private const int Version = 2;

    private static Dictionary<string, EngineLevel>? _cache;

    private static string CachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StreetRodAC", "engine_levels.json");

    private static Dictionary<string, EngineLevel> LoadCache()
    {
        if (_cache != null) return _cache;
        try
        {
            _cache = File.Exists(CachePath)
                ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, EngineLevel>>(File.ReadAllText(CachePath)) ?? new()
                : new();
        }
        catch
        {
            // A cache that does not read is measured again
            _cache = new();
        }

        return _cache;
    }

    private static void SaveCache(Dictionary<string, EngineLevel> cache)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllText(CachePath, System.Text.Json.JsonSerializer.Serialize(cache, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Not remembered: measured again next time, no harm
        }
    }

    #endregion

    private static double? Listen(IntPtr description, float rpm, float throttle)
    {
        Check(FMOD_Studio_EventDescription_CreateInstance(description, out var instance), "create engine");
        try
        {
            FMOD_Studio_EventInstance_SetParameterValue(instance, Utf8("rpms"), rpm);
            FMOD_Studio_EventInstance_SetParameterValue(instance, Utf8("throttle"), throttle * ThrottleMax(description));
            FMOD_Studio_EventInstance_Start(instance);
            FmodPlugins.ClearMeter();

            // Each update mixes one block; the samples load on the way in, and a loop takes a moment to settle
            Mix(WarmUpSeconds);
            FmodPlugins.ResetMeter();
            Mix(ListenSeconds);
            var level = FmodPlugins.MeterRmsDb();

            // Silence is no level: the engine has nothing at this point
            return level is > -90 ? level : null;
        }
        finally
        {
            FMOD_Studio_EventInstance_Stop(instance, StopImmediate);
            FMOD_Studio_EventInstance_Release(instance);
            FMOD_Studio_System_Update(_system);
        }
    }

    /// <summary>
    /// The samples load on a thread of FMOD's own, in real time, while the mixer here runs as fast as it is asked:
    /// without waiting, the first points are measured before there is anything to hear
    /// </summary>
    private static void LoadSamples(IntPtr description)
    {
        Check(FMOD_Studio_EventDescription_LoadSampleData(description), "load samples");
        var until = DateTime.Now.AddSeconds(30);
        while (DateTime.Now < until)
        {
            FMOD_Studio_System_Update(_system);
            if (FMOD_Studio_EventDescription_GetSampleLoadingState(description, out var state) != ResultOk || state != LoadingStateLoading) return;
            Thread.Sleep(5);
        }
    }

    private static void Mix(double seconds)
    {
        // A block is 1024 samples by default; a few too many updates only mix a little more
        var updates = (int)Math.Ceiling(seconds * SampleRate / 1024);
        for (var i = 0; i < updates; i++) FMOD_Studio_System_Update(_system);
    }

    private static float MaxRpm(IntPtr description) => Parameter(description, "rpms") ?? 10000f;

    private static float ThrottleMax(IntPtr description) => Parameter(description, "throttle") ?? 1f;

    private static float? Parameter(IntPtr description, string name)
    {
        if (FMOD_Studio_EventDescription_GetParameterCount(description, out var count) != ResultOk) return null;
        for (var i = 0; i < count; i++)
        {
            if (FMOD_Studio_EventDescription_GetParameterByIndex(description, i, out var parameter) != ResultOk) continue;
            if (string.Equals(System.Runtime.InteropServices.Marshal.PtrToStringUTF8(parameter.Name), name, StringComparison.OrdinalIgnoreCase))
                return parameter.Maximum;
        }

        return null;
    }

    private static void EnsureSystem()
    {
        if (_system != IntPtr.Zero) return;

        Check(FMOD_Studio_System_Create(out var system, HeaderVersion), "create");
        Check(FMOD_Studio_System_GetLowLevelSystem(system, out var core), "core system");
        Check(FMOD_System_SetOutput(core, OutputNoSoundNrt), "silent output");
        Check(FMOD_Studio_System_Initialize(system, 64, StudioInitSynchronousUpdate, InitNormal, IntPtr.Zero), "initialise");
        FmodPlugins.Register(system);

        var common = Path.Combine(Configuration.AppSettings.Instance.AssettoCorsaPath, "content", "sfx", "common.bank");
        Check(FMOD_Studio_System_LoadBankFile(system, Utf8(common), LoadBankNormal, out _), "load common.bank");

        Check(FMOD_System_GetMasterChannelGroup(core, out var master), "master bus");
        Check(FMOD_ChannelGroup_AddDSP(master, DspHead, FmodPlugins.CreateMeter(core)), "meter");

        _system = system;
    }
}
