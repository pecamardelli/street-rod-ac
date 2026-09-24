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

    /// <summary>The shared system, released at exit if it was ever made (<see cref="FmodLifetime"/>)</summary>
    internal static void ShutdownShared()
    {
        if (SharedInstance.IsValueCreated) SharedInstance.Value.Shutdown();
    }

    private const int MaxChannels = 64;

    // A bank being let go of is waited for this long before the race goes ahead and tries to move it anyway
    private static readonly TimeSpan UnloadTimeout = TimeSpan.FromSeconds(5);

    private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("EngineAudio");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IntPtr _system;
    private IntPtr _core;
    private bool _mixerSuspended;
    private bool _shutDown;

    // The AC folder FMOD could not be started from. Not tried again from there, but a new folder in the settings is
    private string? _failedFolder;

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
    /// do to a file that is open, so this goes before every race. Waits for a bank that is still loading, and for
    /// FMOD to have closed the file. The mixer rests until a sound is loaded again: nothing is left to hear, and a
    /// race should not share the audio device with a mixer playing silence. Never throws.
    /// </summary>
    public async Task UnloadAllAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var bank = ReleaseBank();
            if (bank != IntPtr.Zero) await Task.Run(() => WaitUntilUnloaded(bank));
            SuspendMixer();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "The engine sound could not be let go of");
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            Released?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Something that played the engine sound failed on letting go of it");
        }
    }

    /// <summary>Lets FMOD do its work; once a frame while something plays</summary>
    public void Update()
    {
        if (_system != IntPtr.Zero) Warn(FMOD_Studio_System_Update(_system), "update");
    }

    /// <summary>
    /// Releases the FMOD system for good, at exit: what plays, then the banks, then the system. After this nothing
    /// loads. Never throws; waits a moment at most for a load in progress.
    /// </summary>
    internal void Shutdown()
    {
        var entered = _gate.Wait(TimeSpan.FromSeconds(2));
        try
        {
            _shutDown = true;
            ReleaseBank();

            var system = _system;
            _system = IntPtr.Zero;
            _core = IntPtr.Zero;

            // Releasing the system unloads every bank still in it (common.bank) and stops its threads
            if (system != IntPtr.Zero) Warn(FMOD_Studio_System_Release(system), "release");
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "FMOD did not shut down cleanly");
        }
        finally
        {
            if (entered) _gate.Release();
        }
    }

    private EngineVoice? Load(CarSound sound)
    {
        if (!EnsureSystem()) return null;
        ResumeMixer();

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
        if (_shutDown) return false;

        // A folder that failed is not tried again; the player putting the right one in the settings is
        var folder = AppSettings.Instance.AssettoCorsaPath;
        if (string.Equals(_failedFolder, folder, StringComparison.OrdinalIgnoreCase)) return false;

        var system = IntPtr.Zero;
        try
        {
            FmodStudio.Load(folder);
            Check(FMOD_Studio_System_Create(out system, HeaderVersion), "create");
            Check(FMOD_Studio_System_Initialize(system, MaxChannels, StudioInitNormal, InitNormal, IntPtr.Zero), "initialise");

            // Every car bank uses them, and a bank without its plugins does not load
            FmodPlugins.Register(system);

            var common = Path.Combine(folder, "content", "sfx", "common.bank");
            Check(FMOD_Studio_System_LoadBankFile(system, Utf8(common), LoadBankNormal, out _), "load common.bank");

            // The core system is only wanted to rest the mixer between sounds: without it the mixer just runs on
            if (!Warn(FMOD_Studio_System_GetLowLevelSystem(system, out var core), "core system")) core = IntPtr.Zero;

            _system = system;
            _core = core;
            _mixerSuspended = false;
            _failedFolder = null;
            return true;
        }
        catch (Exception ex)
        {
            // Half a system is still threads and memory: it goes, and the next try starts clean
            if (system != IntPtr.Zero) FMOD_Studio_System_Release(system);

            // No sound is no reason to fail a screen: the engine just stays silent
            _failedFolder = folder;
            _logger.Error(ex, "FMOD could not be started from {Folder}: engines will be silent", folder);
            return false;
        }
    }

    private void UnloadBank()
    {
        var bank = ReleaseBank();
        if (bank != IntPtr.Zero) WaitUntilUnloaded(bank);
    }

    /// <summary>Stops the voice and asks FMOD to unload the bank; the bank handle to wait on, or zero</summary>
    private IntPtr ReleaseBank()
    {
        _voice?.Dispose();
        _voice = null;

        var bank = _bank;
        _bank = IntPtr.Zero;
        _bankPath = null;
        if (bank == IntPtr.Zero || _system == IntPtr.Zero) return IntPtr.Zero;

        return Warn(FMOD_Studio_Bank_Unload(bank), "unload bank") ? bank : IntPtr.Zero;
    }

    /// <summary>
    /// The unload is only queued: FMOD runs it on its own thread after an update, and only then is the file closed.
    /// A flush runs the queue there and then, where the DLL has it; either way the bank is watched until it is gone.
    /// </summary>
    private void WaitUntilUnloaded(IntPtr bank)
    {
        if (TryFlushCommands(_system) is { } flushed) Warn(flushed, "flush commands");

        var until = DateTime.Now + UnloadTimeout;
        while (true)
        {
            Update();

            // An invalid handle is a bank that is gone; a DLL that cannot say has had its update and flush
            var asked = TryGetBankLoadingState(bank, out var state);
            if (asked is not { } result || result != ResultOk || state == LoadingStateUnloaded) return;

            if (DateTime.Now >= until)
            {
                _logger.Warning("The engine bank is still {State} after {Seconds} s: moving it aside may fail", state, UnloadTimeout.TotalSeconds);
                return;
            }

            Thread.Sleep(10);
        }
    }

    private void SuspendMixer()
    {
        if (_core == IntPtr.Zero || _mixerSuspended) return;
        if (TryMixerSuspend(_core) is { } result && Warn(result, "suspend mixer")) _mixerSuspended = true;
    }

    private void ResumeMixer()
    {
        if (_core == IntPtr.Zero || !_mixerSuspended) return;
        if (TryMixerResume(_core) is { } result) Warn(result, "resume mixer");
        _mixerSuspended = false;
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
        MaxRpm = ParameterMaximum(_engine, "rpms") ?? DefaultMaxRpm;
        MaxThrottle = ParameterMaximum(_engine, "throttle") ?? DefaultMaxThrottle;
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

    internal string Describe() =>
        $"rpms to {MaxRpm:0}, throttle to {MaxThrottle:0.#}, {(Level == null ? "not measured" : $"evened out {EngineLoudness.GainDb(Level, 800, 0):+0.0;-0.0} dB at idle, {EngineLoudness.GainDb(Level, 3500, 0.5):+0.0;-0.0} at 3500 half, {EngineLoudness.GainDb(Level, 5500, 1):+0.0;-0.0} at 5500 flat out")}{(_limiter != IntPtr.Zero ? ", limiter" : "")}{(_backfire != IntPtr.Zero ? ", backfire" : "")}";

    /// <summary>Reads the samples in now rather than on the first start, which would stutter</summary>
    internal void LoadSamples()
    {
        foreach (var description in new[] { _engine, _limiter, _backfire })
        {
            if (description != IntPtr.Zero) Warn(FMOD_Studio_EventDescription_LoadSampleData(description), "load samples");
        }

        // Samples load on FMOD's own thread; a big bank takes a while
        var until = DateTime.Now.AddSeconds(30);
        while (DateTime.Now < until)
        {
            Warn(FMOD_Studio_System_Update(_system), "update");
            if (FMOD_Studio_EventDescription_GetSampleLoadingState(_engine, out var state) != ResultOk || state != LoadingStateLoading) return;
            Thread.Sleep(10);
        }
    }

    public void Start(float rpm, float throttle)
    {
        if (!Usable || _engineInstance != IntPtr.Zero) return;
        if (!Warn(FMOD_Studio_EventDescription_CreateInstance(_engine, out var instance), "create engine")) return;

        _engineInstance = instance;
        Set(rpm, throttle);
        Warn(FMOD_Studio_EventInstance_Start(instance), "start engine");
    }

    /// <param name="throttle">0..1; scaled to the bank's own range</param>
    public void Set(float rpm, float throttle)
    {
        if (!Usable || _engineInstance == IntPtr.Zero) return;

        Warn(FMOD_Studio_EventInstance_SetParameterValue(_engineInstance, RpmsName, Math.Clamp(rpm, 0f, MaxRpm)), "set rpms");
        Warn(FMOD_Studio_EventInstance_SetParameterValue(_engineInstance, ThrottleName, Math.Clamp(throttle, 0f, 1f) * MaxThrottle), "set throttle");
    }

    /// <param name="volume">0..1, before the bank is evened out</param>
    /// <param name="rpm">Where the engine is: the bank is evened out to the library's middle right here</param>
    /// <param name="throttle">0..1, as given to <see cref="Set"/></param>
    public void SetVolume(float volume, double rpm = 0, double throttle = 0)
    {
        if (!Usable || _engineInstance == IntPtr.Zero) return;

        var gainDb = Level == null ? 0 : EngineLoudness.GainDb(Level, rpm, throttle);
        Warn(FMOD_Studio_EventInstance_SetVolume(_engineInstance, volume * (float)Math.Pow(10, gainDb / 20)), "set volume");
    }

    /// <summary>On the limiter: the bank's own stutter, for as long as the engine bangs against it</summary>
    public void SetLimiter(bool on)
    {
        if (!Usable || _limiter == IntPtr.Zero || on == _limiterOn) return;
        _limiterOn = on;

        if (on)
        {
            if (_limiterInstance == IntPtr.Zero && Warn(FMOD_Studio_EventDescription_CreateInstance(_limiter, out var instance), "create limiter"))
                _limiterInstance = instance;
            if (_limiterInstance != IntPtr.Zero) Warn(FMOD_Studio_EventInstance_Start(_limiterInstance), "start limiter");
        }
        else if (_limiterInstance != IntPtr.Zero)
        {
            Warn(FMOD_Studio_EventInstance_Stop(_limiterInstance, StopAllowFadeout), "stop limiter");
        }
    }

    /// <summary>A pop out of the exhaust: fire and forget</summary>
    public void Backfire(float throttle)
    {
        if (!Usable || _backfire == IntPtr.Zero) return;
        if (!Warn(FMOD_Studio_EventDescription_CreateInstance(_backfire, out var instance), "create backfire")) return;

        Warn(FMOD_Studio_EventInstance_SetParameterValue(instance, ThrottleName, throttle), "set backfire throttle");
        Warn(FMOD_Studio_EventInstance_Start(instance), "start backfire");

        // Released now, it plays out and goes
        Warn(FMOD_Studio_EventInstance_Release(instance), "release backfire");
    }

    public void Stop()
    {
        SetLimiter(false);
        if (_engineInstance == IntPtr.Zero) return;

        if (!_disposed)
        {
            Warn(FMOD_Studio_EventInstance_Stop(_engineInstance, StopAllowFadeout), "stop engine");
            Warn(FMOD_Studio_EventInstance_Release(_engineInstance), "release engine");

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
            Warn(FMOD_Studio_EventInstance_Stop(instance, StopImmediate), "stop engine");
            Warn(FMOD_Studio_EventInstance_Release(instance), "release engine");
        }

        _engineInstance = IntPtr.Zero;
        _limiterInstance = IntPtr.Zero;
        _disposed = true;
    }
}
