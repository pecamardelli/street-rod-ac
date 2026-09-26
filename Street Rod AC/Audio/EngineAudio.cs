using System.IO;
using Street_Rod_AC.Configuration;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Parts.Export;
using static Street_Rod_AC.Audio.FmodStudio;

namespace Street_Rod_AC.Audio;

/// <summary>
/// Plays car engines outside the game, through Assetto Corsa's own FMOD and the same banks a race uses. One FMOD
/// system for the app, and a car bank loaded for each of two channels at most (<see cref="EngineChannel"/>): a bank
/// can be hundreds of MB. Two cars on one sound share its bank, as FMOD cannot load a bank twice.
///
/// FMOD Studio is thread safe as initialised here, so banks load on a worker while the UI thread plays.
/// </summary>
public sealed class EngineAudio
{
    private static readonly Lazy<EngineAudio> SharedInstance = new(() => new EngineAudio());

    public static EngineAudio Shared => SharedInstance.Value;

    /// <summary>The shared system, released at exit if it was ever made (<see cref="FmodLifetime"/>)</summary>
    internal static void ShutdownShared(DateTime deadline)
    {
        if (SharedInstance.IsValueCreated) SharedInstance.Value.Shutdown(deadline);
    }

    private const int MaxChannels = 64;

    // A bank being let go of is waited for this long before the race goes ahead and tries to move it anyway
    private static readonly TimeSpan UnloadTimeout = TimeSpan.FromSeconds(5);

    // A start that failed from a folder is tried again from it after this long: a device that was busy or missing a
    // moment ago may be back, and a whole session of silence is too high a price for one bad moment
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("EngineAudio");

    // Everything that touches the system, the bank or the mixer holds it. Every holder lets go of it off the UI
    // thread (ConfigureAwait(false)), so the exit, which waits for it on the UI thread, never waits on itself
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IntPtr _system;
    private IntPtr _core;
    private bool _mixerSuspended;
    private bool _resumeFailureTold;

    // Set by the exit, read by the worker between FMOD calls
    private volatile bool _shutDown;

    // The AC folder FMOD could not be started from, and when: not tried again from there for a while, but a new
    // folder in the settings is at once
    private string? _failedFolder;
    private DateTime _failedAt;

    /// <summary>A car bank loaded to be heard, known by its path, and its engine</summary>
    private sealed class Channel
    {
        public IntPtr Bank;
        public string? BankPath;
        public EngineVoice? Voice;
    }

    // One a channel: the car on show, and a second one beside it (the Cruise screen's rival). Two channels on the
    // same bank share it, as FMOD loads a bank only once.
    private readonly Channel[] _channels = [new(), new()];

    private EngineAudio() { }

    /// <summary>How loud the garage's engines play, 0..1</summary>
    public float Volume { get; set; } = 1f;

    /// <summary>The loaded bank was let go of (a race is starting): whoever plays it has to load it again</summary>
    public event Action? Released;

    /// <summary>
    /// Loads a car's sound and gets its engine ready to play: the bank, then the engine's samples, so it starts on
    /// the press of a button. Replaces whatever sound was loaded before. Null when the sound cannot be played here.
    /// </summary>
    public async Task<EngineVoice?> LoadAsync(CarSound sound, EngineChannel channel = EngineChannel.Main)
    {
        // Nothing here touches the UI: the gate is let go of on the worker, and the caller still resumes on its own
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Load(sound, _channels[(int)channel])).ConfigureAwait(false);
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
        await UnloadUnderGateAsync();

        // Back on the caller's thread (the UI's): whoever played the sound hears about it there
        try
        {
            Released?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Something that played the engine sound failed on letting go of it");
        }
    }

    private async Task UnloadUnderGateAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            // All of it on the worker, and the gate let go of there too
            await Task.Run(() =>
            {
                foreach (var bank in ReleaseAll()) WaitUntilUnloaded(bank);
                SuspendMixer();
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "The engine sound could not be let go of");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Lets FMOD do its work; once a frame while something plays</summary>
    public void Update()
    {
        if (_system != IntPtr.Zero) Warn(FMOD_Studio_System_Update(_system), "update");
    }

    // The frame FMOD was last updated on: two engines running update it once between them
    private TimeSpan _updatedFrame = TimeSpan.MinValue;

    /// <summary>The frame's update, however many engines ask for it on <paramref name="frame"/> (WPF's rendering time)</summary>
    public void UpdateForFrame(TimeSpan frame)
    {
        if (frame == _updatedFrame) return;
        _updatedFrame = frame;
        Update();
    }

    /// <summary>
    /// Releases the FMOD system for good, at exit: what plays, then the banks, then the system. After this nothing
    /// loads. Never throws. A load or unload in progress sees <see cref="_shutDown"/> and cuts itself short; one that
    /// has not let go of the gate by <paramref name="deadline"/> keeps the system. A system left to the end of the
    /// process is harmless, one freed while a worker is inside it crashes the exit.
    /// </summary>
    internal void Shutdown(DateTime deadline)
    {
        _shutDown = true;

        var entered = false;
        try
        {
            var wait = deadline - DateTime.UtcNow;
            entered = _gate.Wait(wait > TimeSpan.Zero ? wait : TimeSpan.Zero);
            if (!entered)
            {
                _logger.Warning("A sound was still loading or unloading at exit: FMOD is left to the end of the process rather than freed under it");
                return;
            }

            // A rested mixer paces the Studio thread the release waits on (the bank unload just below among it)
            ResumeMixer();
            ReleaseAll();

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

    /// <summary>The exit has begun: the work in progress stops before its next FMOD call</summary>
    private void ThrowIfShutDown()
    {
        if (_shutDown) throw new OperationCanceledException("the app is closing");
    }

    /// <summary>
    /// Lets go of one channel's sound (the Cruise screen's rival, once it is gone): the bank too, unless the other
    /// channel plays it. The other channel plays on. Never throws.
    /// </summary>
    public async Task ReleaseAsync(EngineChannel channel)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                var bank = Release(_channels[(int)channel]);
                if (bank != IntPtr.Zero) WaitUntilUnloaded(bank);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "The {Channel} engine sound could not be let go of", channel);
        }
        finally
        {
            _gate.Release();
        }
    }

    private EngineVoice? Load(CarSound sound, Channel channel)
    {
        if (!EnsureSystem()) return null;
        ResumeMixer();

        // A car racing on another sound keeps its own bank aside; either way this is the file with its bytes
        var bankPath = AcCarSound.OwnBank(sound.BankPath) ?? sound.BankPath;
        if (!File.Exists(bankPath)) throw new FileNotFoundException("No bank", bankPath);

        var events = EngineEvents.Read(sound.GuidsText, sound.DonorId);
        if (events.Engine == null) throw new InvalidDataException("the GUIDs name no engine_ext event");

        if (channel.Voice != null && string.Equals(channel.BankPath, bankPath, StringComparison.OrdinalIgnoreCase)) return channel.Voice;

        var released = Release(channel);
        if (released != IntPtr.Zero) WaitUntilUnloaded(released);
        ThrowIfShutDown();

        var started = DateTime.Now;
        var sharing = _channels.FirstOrDefault(c => c != channel && c.Bank != IntPtr.Zero &&
                                                    string.Equals(c.BankPath, bankPath, StringComparison.OrdinalIgnoreCase));
        IntPtr bank;
        if (sharing != null)
        {
            bank = sharing.Bank;
        }
        else
        {
            var result = FMOD_Studio_System_LoadBankFile(_system, Utf8(bankPath), LoadBankNormal, out bank);
            if (result != ResultOk)
            {
                // The same bank from another file (a library sound and a car's own copy of it): FMOD knows a bank by
                // its id, not its path, and will not load it twice. The other channel has it loaded already
                sharing = SameBankElsewhere(channel, events.Engine.Value);
                if (sharing == null) throw new FmodException("load " + Path.GetFileName(bankPath), result);
                bank = sharing.Bank;
                _logger.Information("{Bank} is the bank the other engine plays already: shared", Path.GetFileName(bankPath));
            }
        }

        channel.Bank = bank;
        channel.BankPath = bankPath;

        var voice = new EngineVoice(this, _system, events);
        voice.LoadSamples(() => _shutDown);
        ThrowIfShutDown();
        voice.Level = sharing?.Voice?.Level ?? MeasureLevel(bankPath, events.Engine.Value);
        channel.Voice = voice;

        _logger.Information("Engine sound {Bank} ready in {Ms} ms ({Params})", Path.GetFileName(bankPath),
            (int)(DateTime.Now - started).TotalMilliseconds, voice.Describe());
        return voice;
    }

    /// <summary>The other channel, when its bank holds <paramref name="engine"/>: FMOD finds the event only in a loaded bank</summary>
    private Channel? SameBankElsewhere(Channel channel, Guid engine)
    {
        var other = _channels.FirstOrDefault(c => c != channel && c.Bank != IntPtr.Zero);
        if (other == null) return null;
        return FMOD_Studio_System_GetEventByID(_system, ref engine, out _) == ResultOk ? other : null;
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

        // A folder that failed is not tried again for a while; the player putting the right one in the settings is
        var folder = AppSettings.Instance.AssettoCorsaPath;
        if (string.Equals(_failedFolder, folder, StringComparison.OrdinalIgnoreCase) && DateTime.UtcNow - _failedAt < RetryAfter) return false;

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
            _resumeFailureTold = false;
            _failedFolder = null;
            return true;
        }
        catch (Exception ex)
        {
            // Half a system is still threads and memory: it goes, and the next try starts clean
            if (system != IntPtr.Zero) FMOD_Studio_System_Release(system);

            // No sound is no reason to fail a screen: the engine just stays silent
            _failedFolder = folder;
            _failedAt = DateTime.UtcNow;
            _logger.Error(ex, "FMOD could not be started from {Folder}: engines are silent for now (tried again in {Minutes} min)", folder, RetryAfter.TotalMinutes);
            return false;
        }
    }

    /// <summary>Every channel let go of; the bank handles to wait on</summary>
    private List<IntPtr> ReleaseAll()
    {
        var banks = new List<IntPtr>();
        foreach (var channel in _channels)
        {
            var bank = Release(channel);
            if (bank != IntPtr.Zero) banks.Add(bank);
        }

        return banks;
    }

    /// <summary>
    /// Stops the channel's voice and asks FMOD to unload its bank, unless the other channel still plays it; the bank
    /// handle to wait on, or zero
    /// </summary>
    private IntPtr Release(Channel channel)
    {
        channel.Voice?.Dispose();
        channel.Voice = null;

        var bank = channel.Bank;
        channel.Bank = IntPtr.Zero;
        channel.BankPath = null;
        if (bank == IntPtr.Zero || _system == IntPtr.Zero) return IntPtr.Zero;
        if (_channels.Any(c => c.Bank == bank)) return IntPtr.Zero;

        return Warn(FMOD_Studio_Bank_Unload(bank), "unload bank") ? bank : IntPtr.Zero;
    }

    /// <summary>
    /// The unload is only queued: FMOD runs it on its own thread after an update, and only then is the file closed.
    /// A flush runs the queue there and then, where the DLL has it; either way the bank is watched until it is gone.
    /// Cut short at exit: the file no longer has to be free for a race.
    /// </summary>
    private void WaitUntilUnloaded(IntPtr bank)
    {
        if (TryFlushCommands(_system) is { } flushed) Warn(flushed, "flush commands");

        var until = DateTime.Now + UnloadTimeout;
        while (!_shutDown)
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

    // The mixer is rested and woken only under the gate, so never both at once. The two may run on different pool
    // threads: FMOD's core API is thread safe as initialised here (no FMOD_INIT_THREAD_UNSAFE) and serialises calls.
    // A suspend that fails is logged by Warn and leaves the flag down: the mixer just runs on.
    private void SuspendMixer()
    {
        if (_core == IntPtr.Zero || _mixerSuspended) return;
        if (TryMixerSuspend(_core) is { } result && Warn(result, "suspend mixer")) _mixerSuspended = true;
    }

    /// <summary>
    /// Wakes the mixer. The flag drops only once it is awake, so a failed wake is tried again by the next load. A
    /// mixer left asleep is a mute garage, so the first failure is also said plainly, to be found in the log.
    /// </summary>
    private void ResumeMixer()
    {
        if (_core == IntPtr.Zero || !_mixerSuspended) return;

        var result = TryMixerResume(_core);
        if (result is { } code && Warn(code, "resume mixer"))
        {
            _mixerSuspended = false;
            return;
        }

        if (_resumeFailureTold) return;
        _resumeFailureTold = true;
        _logger.Information("The FMOD mixer could not be woken ({Reason}): garage engines stay silent until it is",
            result is { } failed ? $"error {failed}" : "the DLL has no FMOD_System_MixerResume");
    }

    /// <summary>A voice that is no longer the loaded one must not touch FMOD</summary>
    internal bool IsCurrent(EngineVoice voice) => _channels.Any(c => ReferenceEquals(c.Voice, voice));
}

/// <summary>Where an engine plays: the car on show, or a second car beside it</summary>
public enum EngineChannel
{
    Main,
    Second
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

    // The voice is driven from the UI thread, but a bank released before a race disposes it on a pool thread:
    // an instance made while that happens is let go by whoever takes it out of its field first (TakeOver)
    private volatile bool _disposed;

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

    /// <summary>Reads the samples in now rather than on the first start, which would stutter; stops waiting once <paramref name="stop"/> says so</summary>
    internal void LoadSamples(Func<bool> stop)
    {
        foreach (var description in new[] { _engine, _limiter, _backfire })
        {
            if (description != IntPtr.Zero) Warn(FMOD_Studio_EventDescription_LoadSampleData(description), "load samples");
        }

        // Samples load on FMOD's own thread; a big bank takes a while
        var until = DateTime.Now.AddSeconds(30);
        while (DateTime.Now < until && !stop())
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
        if (_disposed)
        {
            ReleaseNow(ref _engineInstance, "engine");
            return;
        }

        Set(rpm, throttle);
        Warn(FMOD_Studio_EventInstance_Start(instance), "start engine");
    }

    /// <summary>Stops and releases the instance in <paramref name="field"/> if it is still there to take</summary>
    private static void ReleaseNow(ref IntPtr field, string what)
    {
        var instance = Interlocked.Exchange(ref field, IntPtr.Zero);
        if (instance == IntPtr.Zero) return;

        Warn(FMOD_Studio_EventInstance_Stop(instance, StopImmediate), "stop " + what);
        Warn(FMOD_Studio_EventInstance_Release(instance), "release " + what);
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

    /// <summary>
    /// Where the engine is heard from, as a direction from the listener (x right, y up, z ahead): only the way it
    /// comes from, never how far, which the caller's volume says. The point is put a metre off, inside any event's
    /// own distance falloff. An event made without 3D takes no notice.
    /// </summary>
    public void SetDirection(float x, float y, float z)
    {
        if (!Usable || _engineInstance == IntPtr.Zero) return;

        var length = MathF.Sqrt(x * x + y * y + z * z);
        if (length < 1e-4f) (x, y, z, length) = (0f, 0f, 1f, 1f);
        var attributes = new Attributes3D
        {
            Position = new Vector { X = x / length, Y = y / length, Z = z / length },
            Forward = new Vector { Z = 1f },
            Up = new Vector { Y = 1f }
        };
        Warn(FMOD_Studio_EventInstance_Set3DAttributes(_engineInstance, ref attributes), "set 3d attributes");
    }

    /// <summary>On the limiter: the bank's own stutter, for as long as the engine bangs against it</summary>
    public void SetLimiter(bool on)
    {
        if (!Usable || _limiter == IntPtr.Zero || on == _limiterOn) return;
        _limiterOn = on;

        if (on)
        {
            if (_limiterInstance == IntPtr.Zero && Warn(FMOD_Studio_EventDescription_CreateInstance(_limiter, out var instance), "create limiter"))
            {
                _limiterInstance = instance;
                if (_disposed)
                {
                    ReleaseNow(ref _limiterInstance, "limiter");
                    return;
                }
            }
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

        // Taken out of the field: whoever takes it releases it, this or Dispose, never both
        var instance = Interlocked.Exchange(ref _engineInstance, IntPtr.Zero);
        if (instance == IntPtr.Zero) return;

        Warn(FMOD_Studio_EventInstance_Stop(instance, StopAllowFadeout), "stop engine");
        Warn(FMOD_Studio_EventInstance_Release(instance), "release engine");

        // FMOD acts on the stop at its next update, and the runner's clock may already be stopping: without one
        // here the engine goes on sounding, frozen at its last rpm
        if (!_disposed) _owner.Update();
    }

    public void Dispose()
    {
        if (_disposed) return;

        // Flagged first: an instance the UI thread makes from here on is released by the UI thread itself
        _disposed = true;
        ReleaseNow(ref _engineInstance, "engine");
        ReleaseNow(ref _limiterInstance, "limiter");
    }
}
