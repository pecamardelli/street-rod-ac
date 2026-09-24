using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Street_Rod_AC.Logging;

namespace Street_Rod_AC.Audio;

/// <summary>
/// A car's engine you can start and rev where the car stands: the simulation, its sound, and the pedal. Lives on
/// the UI thread and runs off the frame clock while there is anything to hear or see.
/// </summary>
public sealed class EngineRunner : INotifyPropertyChanged
{
    // Pedal: a key pushes it down and lets it up this fast, a share of the way per second
    private const double KeyPressRate = 9;
    private const double KeyReleaseRate = 12;

    // The engine is heard from beside the car, not from the driver's seat
    private const float Volume = 0.9f;
    private const double FadeOutRpm = 450;

    private const double MaxStep = 1.0 / 60;
    private const double MaxCatchUp = 0.25;

    private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("EngineAudio");
    private EngineSpec? _spec;
    private EngineSim? _sim;
    private EngineVoice? _voice;
    private int _loadVersion;
    private bool _ticking;
    private TimeSpan _lastFrame;
    private bool _keyDown;
    private double? _pedalHeld;
    private double _pedal;
    private bool _listening;
    private bool _soundFailed;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Every frame the body moved; the viewport draws on it</summary>
    public event Action? PoseChanged;

    public EngineSpec? Spec => _spec;

    public bool HasEngine => _spec != null;

    public bool IsLoading { get; private set; }

    public EngineState State => _sim?.State ?? EngineState.Off;

    public bool IsRunning => State is EngineState.Cranking or EngineState.Running;

    public bool CanToggle => _spec != null;

    public double Rpm { get; private set; }

    /// <summary>Where the needle is: 0 at a stop, 1 a little past the limiter</summary>
    public double RpmFraction => _sim == null ? 0 : Math.Clamp(Rpm / TachMax, 0, 1);

    public double RedlineFraction => _sim == null ? 0.9 : Math.Clamp(_sim.Limiter / TachMax, 0, 1);

    private double TachMax => (_sim?.Limiter ?? 6000) * 1.12;

    public double Pedal => _pedal;

    public string RpmDisplay => $"{Rpm:0}";

    public string StatusText => IsLoading ? "Getting the engine ready..."
        : _spec == null ? "No engine"
        : _sim?.FailedToStart == true ? "It won't start: " + _spec.Problem
        : State switch
        {
            EngineState.Cranking => "Cranking...",
            EngineState.Running => _sim!.OnLimiter ? "On the limiter!" : "Running",
            EngineState.Stopping => "Shutting down",
            _ => _spec.Sound == null || _soundFailed ? "Off (no sound)" : "Off"
        };

    public BodyPose Pose => _sim?.Pose ?? BodyPose.Rest;

    /// <summary>The engine is running or the body is still settling: a viewport has to keep drawing</summary>
    public bool IsActive => _sim != null && !_sim.IsAtRest;

    /// <summary>
    /// Puts a car's engine under the pedal; null when there is no car or it has no engine. The engine that was
    /// there is switched off at once. The sound loads in the background, and the engine can start before it
    /// arrives, silently.
    /// </summary>
    public async Task SetEngineAsync(EngineSpec? spec)
    {
        if (ReferenceEquals(spec, _spec)) return;

        StopNow();
        _spec = spec;
        _sim = spec == null ? null : NewSim(spec);
        _voice = null;
        Listen(spec != null);
        NotifyAll();

        await LoadSoundAsync();
    }

    private async Task LoadSoundAsync()
    {
        var version = ++_loadVersion;
        var sound = _spec?.Sound;
        IsLoading = sound != null;
        NotifyAll();
        if (sound == null) return;

        var voice = await EngineAudio.Shared.LoadAsync(sound);
        if (version != _loadVersion) return;

        _voice = voice;
        _soundFailed = voice == null;
        IsLoading = false;

        // Started while the sound was on its way: it joins in
        if (IsRunning) _voice?.Start((float)Rpm, (float)(_sim?.Load ?? 0));
        NotifyAll();
    }

    private void Listen(bool on)
    {
        if (on == _listening) return;
        _listening = on;
        if (on) EngineAudio.Shared.Released += OnSoundReleased;
        else EngineAudio.Shared.Released -= OnSoundReleased;
    }

    /// <summary>A race took the sound away: the engine goes off, and the sound comes back when it is next started</summary>
    private void OnSoundReleased()
    {
        StopNow();
        _loadVersion++;
        _voice = null;
        IsLoading = false;
        NotifyAll();
    }

    private EngineSim NewSim(EngineSpec spec)
    {
        var sim = new EngineSim(spec);
        sim.Backfired += pulling => _voice?.Backfire(pulling);
        return sim;
    }

    public void Toggle()
    {
        if (!CanToggle || _sim == null) return;

        if (IsRunning)
        {
            _sim.Stop();
        }
        else
        {
            if (_voice == null && !IsLoading && _spec?.Sound != null) _ = LoadSoundAsync();
            _sim.Start();
            _voice?.Start((float)_sim.Rpm, 0f);
            _voice?.SetVolume(0f);
            _logger.Information("Starting {Engine}: idle {Idle:0}, limiter {Limiter:0}, inertia {Inertia:0.###}",
                _spec!.Name, _sim.Idle, _sim.Limiter, _sim.Inertia);
        }

        StartTicking();
        NotifyAll();
    }

    /// <summary>The throttle key is down or up</summary>
    public void SetKey(bool down)
    {
        _keyDown = down;
        if (down) StartTicking();
    }

    /// <summary>The on-screen pedal, pressed this far (0..1); null when it is let go</summary>
    public void SetPedal(double? depth)
    {
        _pedalHeld = depth is { } d ? Math.Clamp(d, 0, 1) : null;
        if (depth != null) StartTicking();
    }

    /// <summary>Switches off and goes silent at once: the car is changing, or the screen is going</summary>
    public void StopNow()
    {
        _voice?.Stop();
        _keyDown = false;
        _pedalHeld = null;
        _pedal = 0;
        if (_spec != null) _sim = NewSim(_spec);
        Rpm = 0;
        StopTicking();
        NotifyAll();
        PoseChanged?.Invoke();
    }

    /// <summary>Lets go of the car and its sound, for good</summary>
    public void Detach()
    {
        StopNow();
        Listen(false);
        _loadVersion++;
        _spec = null;
        _sim = null;
        _voice = null;
        IsLoading = false;
        NotifyAll();
    }

    private void StartTicking()
    {
        if (_ticking) return;
        _ticking = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    private void StopTicking()
    {
        if (!_ticking) return;
        _ticking = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastFrame) return;

        var dt = _lastFrame == TimeSpan.Zero ? 1.0 / 60 : (now - _lastFrame).TotalSeconds;
        _lastFrame = now;
        Tick(dt);
    }

    private void Tick(double dt)
    {
        var sim = _sim;
        if (sim == null)
        {
            StopTicking();
            return;
        }

        var wasState = sim.State;
        var target = _pedalHeld ?? (_keyDown ? 1 : 0);
        var rate = target > _pedal ? KeyPressRate : KeyReleaseRate;
        _pedal += (target - _pedal) * Math.Min(1, dt * rate);
        if (_pedalHeld != null) _pedal = _pedalHeld.Value;

        // A slow frame is several steps, so the engine keeps real time; a stall of the UI is not caught up on
        var left = Math.Min(dt, MaxCatchUp);
        while (left > 1e-6)
        {
            var step = Math.Min(left, MaxStep);
            sim.Tick(step, sim.State == EngineState.Running ? _pedal : 0);
            left -= step;
        }

        Rpm = sim.Rpm;

        if (_voice != null)
        {
            if (sim.State == EngineState.Off)
            {
                _voice.Stop();
            }
            else
            {
                _voice.Set((float)Rpm, (float)sim.Load);
                _voice.SetVolume(Volume * EngineAudio.Shared.Volume * (float)Math.Clamp(Rpm / FadeOutRpm, 0, 1), Rpm, sim.Load);
                _voice.SetLimiter(sim.OnLimiter);
            }
        }

        EngineAudio.Shared.Update();

        Notify(nameof(Rpm));
        Notify(nameof(RpmDisplay));
        Notify(nameof(RpmFraction));
        Notify(nameof(Pedal));
        if (wasState != sim.State || sim.OnLimiter || sim.FailedToStart) NotifyAll();
        PoseChanged?.Invoke();

        // Nothing left moving and nothing held: the clock can rest
        if (sim.IsAtRest && _pedal < 1e-3 && !_keyDown && _pedalHeld == null) StopTicking();
    }

    private void NotifyAll()
    {
        foreach (var name in new[]
                 {
                     nameof(Spec), nameof(HasEngine), nameof(IsLoading), nameof(State), nameof(IsRunning), nameof(CanToggle),
                     nameof(Rpm), nameof(RpmDisplay), nameof(RpmFraction), nameof(RedlineFraction), nameof(Pedal), nameof(StatusText),
                     nameof(IsActive)
                 })
        {
            Notify(name);
        }
    }

    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
