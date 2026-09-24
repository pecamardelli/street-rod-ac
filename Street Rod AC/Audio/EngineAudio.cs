using System.IO;
using Street_Rod_AC.Configuration;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Parts.Export;
using static Street_Rod_AC.Audio.FmodStudio;

namespace Street_Rod_AC.Audio;

/// <summary>
/// Plays car engines outside the game, through Assetto Corsa's own FMOD and the same banks a race uses. One FMOD
/// system for the app, one car bank loaded at a time: two copies of a bank (a sound shared by two cars) cannot be
/// loaded side by side, and a bank can be hundreds of MB.
///
/// FMOD Studio is thread safe as initialised here, so banks load on a worker while the UI thread plays.
/// </summary>
public sealed class EngineAudio
{
    private static readonly Lazy<EngineAudio> SharedInstance = new(() => new EngineAudio());

    public static EngineAudio Shared => SharedInstance.Value;

    private const int MaxChannels = 64;

    private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("EngineAudio");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IntPtr _system;
    private bool _failed;

    // The car bank that is loaded, known by its path
    private IntPtr _bank;
    private string? _bankPath;
    private EngineVoice? _voice;

    private EngineAudio() { }

    /// <summary>How loud the garage's engines play, 0..1</summary>
    public float Volume { get; set; } = 1f;

    /// <summary>The loaded bank was let go of (a race is starting): whoever plays it has to load it again</summary>
    public event Action? Released;

    /// <summary>
    /// Loads a car's sound and gets its engine ready to play: the bank, then the engine's samples, so it starts on
    /// the press of a button. Replaces whatever sound was loaded before. Null when the sound cannot be played here.
    /// </summary>
    public async Task<EngineVoice?> LoadAsync(CarSound sound)
    {
        await _gate.WaitAsync();
        try
        {
            return await Task.Run(() => Load(sound));
        }
        catch (Exception ex)
        {
            _logger.Warning("The sound of {Donor} cannot be played in the garage: {Error}", sound.DonorId, ex.Message);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Lets go of the car bank. A race moves the car's bank aside for the sound it races with, which Windows will not
    /// do to a file that is open, so this goes before every race. Waits for a bank that is still loading.
    /// </summary>
    public async Task UnloadAllAsync()
    {
        await _gate.WaitAsync();
        try
        {
            UnloadBank();
        }
        finally
        {
            _gate.Release();
        }

        Released?.Invoke();
    }

    /// <summary>Lets FMOD do its work; once a frame while something plays</summary>
    public void Update()
    {
        if (_system != IntPtr.Zero) FMOD_Studio_System_Update(_system);
    }

    private EngineVoice? Load(CarSound sound)
    {
        if (!EnsureSystem()) return null;

        // A car racing on another sound keeps its own bank aside; either way this is the file with its bytes
        var bankPath = AcCarSound.OwnBank(sound.BankPath) ?? sound.BankPath;
        if (!File.Exists(bankPath)) throw new FileNotFoundException("No bank", bankPath);

        var events = EngineEvents.Read(sound.GuidsText, sound.DonorId);
        if (events.Engine == null) throw new InvalidDataException("the GUIDs name no engine_ext event");

        if (_voice != null && string.Equals(_bankPath, bankPath, StringComparison.OrdinalIgnoreCase)) return _voice;

        UnloadBank();

        var started = DateTime.Now;
        Check(FMOD_Studio_System_LoadBankFile(_system, Utf8(bankPath), LoadBankNormal, out var bank), "load " + Path.GetFileName(bankPath));
        _bank = bank;
        _bankPath = bankPath;

        var voice = new EngineVoice(this, _system, events);
        voice.LoadSamples();
        voice.Level = MeasureLevel(bankPath, events.Engine.Value);
        _voice = voice;

        _logger.Information("Engine sound {Bank} ready in {Ms} ms ({Params})", Path.GetFileName(bankPath),
            (int)(DateTime.Now - started).TotalMilliseconds, voice.Describe());
        return voice;
    }

    // Evening the sound out is a nicety: a bank that cannot be measured plays as its author mixed it
    private EngineLevel? MeasureLevel(string bankPath, Guid engine)
    {
        try
        {
            return EngineLoudness.Measure(bankPath, engine);
        }
        catch (Exception ex)
        {
            _logger.Warning("{Bank} could not be measured, it plays as it is: {Error}", Path.GetFileName(bankPath), ex.Message);
            return null;
        }
    }

    private bool EnsureSystem()
    {
        if (_system != IntPtr.Zero) return true;
        if (_failed) return false;

        try
        {
            var folder = AppSettings.Instance.AssettoCorsaPath;
            FmodStudio.Load(folder);
            Check(FMOD_Studio_System_Create(out var system, HeaderVersion), "create");
            Check(FMOD_Studio_System_Initialize(system, MaxChannels, StudioInitNormal, InitNormal, IntPtr.Zero), "initialise");

            // Every car bank uses them, and a bank without its plugins does not load
            FmodPlugins.Register(system);

            var common = Path.Combine(folder, "content", "sfx", "common.bank");
            Check(FMOD_Studio_System_LoadBankFile(system, Utf8(common), LoadBankNormal, out _), "load common.bank");

            _system = system;
            return true;
        }
        catch (Exception ex)
        {
            // No sound is no reason to fail a screen: the engine just stays silent
            _failed = true;
            _logger.Error(ex, "FMOD could not be started: engines will be silent");
            return false;
        }
    }

    private void UnloadBank()
    {
        _voice?.Dispose();
        _voice = null;

        if (_bank != IntPtr.Zero) FMOD_Studio_Bank_Unload(_bank);
        _bank = IntPtr.Zero;
        _bankPath = null;

        // The unload is only done once FMOD has had its update
        Update();
    }

    /// <summary>A voice that is no longer the loaded one must not touch FMOD</summary>
    internal bool IsCurrent(EngineVoice voice) => ReferenceEquals(_voice, voice);
}

/// <summary>The event ids a car's engine needs out of its GUIDs text</summary>
public sealed record EngineEvents(Guid? Engine, Guid? Limiter, Guid? Backfire)
{
    public static EngineEvents Read(string guidsText, string donorId)
    {
        Guid? engine = null, limiter = null, backfire = null;
        Guid? anyEngine = null;

        foreach (var raw in guidsText.Split('\n'))
        {
            var line = raw.Trim();
            var space = line.IndexOf(' ');
            if (space <= 0 || !Guid.TryParse(line[..space], out var id)) continue;

            var path = line[(space + 1)..].Trim();
            if (!path.StartsWith("event:/cars/", StringComparison.OrdinalIgnoreCase)) continue;

            var parts = path.Split('/');
            if (parts.Length != 4) continue;

            var mine = parts[2].Equals(donorId, StringComparison.OrdinalIgnoreCase);
            switch (parts[3].ToLowerInvariant())
            {
                case "engine_ext":
                    anyEngine ??= id;
                    if (mine) engine = id;
                    break;
                case "limiter" when mine:
                    limiter = id;
                    break;
                case "backfire_ext" when mine:
                    backfire = id;
                    break;
            }
        }

        return new EngineEvents(engine ?? anyEngine, limiter, backfire);
    }
}

/// <summary>
/// A loaded engine sound: the running engine, the limiter and the pops on a lift. Played from the UI thread.
/// </summary>
public sealed class EngineVoice : IDisposable
{
    private static readonly byte[] RpmsName = Utf8("rpms");
    private static readonly byte[] ThrottleName = Utf8("throttle");

    private readonly EngineAudio _owner;
    private readonly IntPtr _system;
    private readonly IntPtr _engine;
    private readonly IntPtr _limiter;
    private readonly IntPtr _backfire;
    private IntPtr _engineInstance;
    private IntPtr _limiterInstance;
    private bool _limiterOn;
    private bool _disposed;

    internal EngineVoice(EngineAudio owner, IntPtr system, EngineEvents events)
    {
        _owner = owner;
        _system = system;
        _engine = Description(events.Engine) ?? throw new InvalidDataException("the bank has no engine_ext event");
        _limiter = Description(events.Limiter) ?? IntPtr.Zero;
        _backfire = Description(events.Backfire) ?? IntPtr.Zero;

        // Highest rpm the samples go to: past it the bank has nothing new to say
        MaxRpm = ParameterMaximum(_engine, "rpms") ?? 10000f;
        MaxThrottle = ParameterMaximum(_engine, "throttle") ?? 1f;
    }

    public float MaxRpm { get; }

    /// <summary>Some banks run throttle past 1; full throttle is this</summary>
    public float MaxThrottle { get; }

    public bool IsPlaying => _engineInstance != IntPtr.Zero;

    /// <summary>How loud the bank plays of itself, over its range; null when it could not be measured (it then plays as it is)</summary>
    public EngineLevel? Level { get; internal set; }

    private bool Usable => !_disposed && _owner.IsCurrent(this);

    private IntPtr? Description(Guid? id)
    {
        if (id is not { } guid) return null;
        return FMOD_Studio_System_GetEventByID(_system, ref guid, out var description) == ResultOk ? description : null;
    }

    private static float? ParameterMaximum(IntPtr description, string name)
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

    internal string Describe() =>
        $"rpms to {MaxRpm:0}, throttle to {MaxThrottle:0.#}, {(Level == null ? "not measured" : $"evened out {EngineLoudness.GainDb(Level, 800, 0):+0.0;-0.0} dB at idle, {EngineLoudness.GainDb(Level, 3500, 0.5):+0.0;-0.0} at 3500 half, {EngineLoudness.GainDb(Level, 5500, 1):+0.0;-0.0} at 5500 flat out")}{(_limiter != IntPtr.Zero ? ", limiter" : "")}{(_backfire != IntPtr.Zero ? ", backfire" : "")}";

    /// <summary>Reads the samples in now rather than on the first start, which would stutter</summary>
    internal void LoadSamples()
    {
        foreach (var description in new[] { _engine, _limiter, _backfire })
        {
            if (description != IntPtr.Zero) FMOD_Studio_EventDescription_LoadSampleData(description);
        }

        // Samples load on FMOD's own thread; a big bank takes a while
        var until = DateTime.Now.AddSeconds(30);
        while (DateTime.Now < until)
        {
            FMOD_Studio_System_Update(_system);
            if (FMOD_Studio_EventDescription_GetSampleLoadingState(_engine, out var state) != ResultOk || state != LoadingStateLoading) return;
            Thread.Sleep(10);
        }
    }

    public void Start(float rpm, float throttle)
    {
        if (!Usable || _engineInstance != IntPtr.Zero) return;
        if (FMOD_Studio_EventDescription_CreateInstance(_engine, out var instance) != ResultOk) return;

        _engineInstance = instance;
        Set(rpm, throttle);
        FMOD_Studio_EventInstance_Start(instance);
    }

    /// <param name="throttle">0..1; scaled to the bank's own range</param>
    public void Set(float rpm, float throttle)
    {
        if (!Usable || _engineInstance == IntPtr.Zero) return;

        FMOD_Studio_EventInstance_SetParameterValue(_engineInstance, RpmsName, Math.Clamp(rpm, 0f, MaxRpm));
        FMOD_Studio_EventInstance_SetParameterValue(_engineInstance, ThrottleName, Math.Clamp(throttle, 0f, 1f) * MaxThrottle);
    }

    /// <param name="volume">0..1, before the bank is evened out</param>
    /// <param name="rpm">Where the engine is: the bank is evened out to the library's middle right here</param>
    /// <param name="throttle">0..1, as given to <see cref="Set"/></param>
    public void SetVolume(float volume, double rpm = 0, double throttle = 0)
    {
        if (!Usable || _engineInstance == IntPtr.Zero) return;

        var gainDb = Level == null ? 0 : EngineLoudness.GainDb(Level, rpm, throttle);
        FMOD_Studio_EventInstance_SetVolume(_engineInstance, volume * (float)Math.Pow(10, gainDb / 20));
    }

    /// <summary>On the limiter: the bank's own stutter, for as long as the engine bangs against it</summary>
    public void SetLimiter(bool on)
    {
        if (!Usable || _limiter == IntPtr.Zero || on == _limiterOn) return;
        _limiterOn = on;

        if (on)
        {
            if (_limiterInstance == IntPtr.Zero && FMOD_Studio_EventDescription_CreateInstance(_limiter, out var instance) == ResultOk)
                _limiterInstance = instance;
            if (_limiterInstance != IntPtr.Zero) FMOD_Studio_EventInstance_Start(_limiterInstance);
        }
        else if (_limiterInstance != IntPtr.Zero)
        {
            FMOD_Studio_EventInstance_Stop(_limiterInstance, StopAllowFadeout);
        }
    }

    /// <summary>A pop out of the exhaust: fire and forget</summary>
    public void Backfire(float throttle)
    {
        if (!Usable || _backfire == IntPtr.Zero) return;
        if (FMOD_Studio_EventDescription_CreateInstance(_backfire, out var instance) != ResultOk) return;

        FMOD_Studio_EventInstance_SetParameterValue(instance, ThrottleName, throttle);
        FMOD_Studio_EventInstance_Start(instance);

        // Released now, it plays out and goes
        FMOD_Studio_EventInstance_Release(instance);
    }

    public void Stop()
    {
        SetLimiter(false);
        if (_engineInstance == IntPtr.Zero) return;

        if (!_disposed)
        {
            FMOD_Studio_EventInstance_Stop(_engineInstance, StopAllowFadeout);
            FMOD_Studio_EventInstance_Release(_engineInstance);

            // FMOD acts on the stop at its next update, and the runner's clock may already be stopping: without one
            // here the engine goes on sounding, frozen at its last rpm
            _owner.Update();
        }

        _engineInstance = IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var instance in new[] { _engineInstance, _limiterInstance })
        {
            if (instance == IntPtr.Zero) continue;
            FMOD_Studio_EventInstance_Stop(instance, StopImmediate);
            FMOD_Studio_EventInstance_Release(instance);
        }

        _engineInstance = IntPtr.Zero;
        _limiterInstance = IntPtr.Zero;
        _disposed = true;
    }
}
