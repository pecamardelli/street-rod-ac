using Street_Rod_AC.Parts.Export;

namespace Street_Rod_AC.Audio;

/// <summary>What a car's engine is, for starting it up and revving it where it stands</summary>
public sealed record EngineSpec(
    string Name,
    double IdleRpm,
    double LimiterRpm,
    int Cylinders,
    double Inertia,
    Func<double, double> TorqueAt,
    double PeakTorque,
    CarSound? Sound,
    string? Problem)
{
    /// <summary>The engine turns over but will not fire</summary>
    public bool Runs => Problem == null;
}

public enum EngineState
{
    Off,
    Cranking,
    Running,
    Stopping
}

/// <summary>How the body sits on its springs right now, in radians and metres</summary>
public readonly record struct BodyPose(float Roll, float Pitch, float Heave)
{
    public static readonly BodyPose Rest = new(0f, 0f, 0f);

    public bool IsRest => Math.Abs(Roll) < 1e-5f && Math.Abs(Pitch) < 1e-5f && Math.Abs(Heave) < 1e-5f;
}

/// <summary>
/// An engine out of gear: the crank and flywheel spun up by what the cylinders make and slowed by what the engine
/// costs itself. Torque comes off the car's own dyno curve, times how far the throttle lets air in. An idle control
/// holds the idle; the limiter cuts the spark. The body rocks against the crank: whatever speeds the flywheel up
/// twists the block the other way, so a blip kicks the car over and a lift lets it rock back.
/// </summary>
public sealed class EngineSim
{
    // Starter: what it spins the engine to, and how long it takes to catch
    private const double CrankRpm = 230;
    private const double CrankSeconds = 0.75;
    private const double CrankNoStartSeconds = 2.4;

    // The engine flares past idle as it catches, then settles
    private const double CatchFlareSeconds = 0.45;
    private const double CatchFlareLoad = 0.22;

    // The throttle plate and manifold do not fill at once
    private const double IntakeLag = 0.07;

    // What the engine costs itself: friction growing with speed, pumping against a closed throttle
    private const double FrictionBase = 0.05;
    private const double FrictionSquare = 0.2;
    private const double PumpingLoss = 0.16;

    private const double IdleGain = 1.6;

    // The blocks' inertia figures are the source game's own (5 to 10 for an eight), not kg m2: this brings them
    // to what a crank and flywheel weigh in at, so a blip takes the time it does on a real engine out of gear
    private const double InertiaScale = 0.05;
    private const double LimiterHysteresisRpm = 180;
    private const double StoppedRpm = 90;

    private readonly EngineSpec _spec;
    private readonly BodyRock _body;
    private readonly Random _random = new();
    private double _time;
    private double _stateTime;
    private double _load;
    private bool _cut;
    private double _lastCut = double.MinValue;
    private double _peakLoadSinceLift;

    public EngineSim(EngineSpec spec)
    {
        _spec = spec;
        Idle = spec.IdleRpm > 200 ? spec.IdleRpm : 800;
        Limiter = spec.LimiterRpm > Idle + 1000 ? spec.LimiterRpm : 6000;
        Inertia = Math.Clamp(spec.Inertia > 0 ? spec.Inertia * InertiaScale : 0.3, 0.08, 0.8);
        PeakTorque = spec.PeakTorque > 1 ? spec.PeakTorque : 300;
        _body = new BodyRock(spec.Cylinders);
    }

    public double Idle { get; }
    public double Limiter { get; }
    public double Inertia { get; }
    public double PeakTorque { get; }

    public EngineState State { get; private set; } = EngineState.Off;

    public double Rpm { get; private set; }

    /// <summary>How much the engine is being asked for, 0..1: the sound's throttle</summary>
    public double Load => _load;

    /// <summary>Banging against the limiter just now</summary>
    public bool OnLimiter => _time - _lastCut < 0.2;

    /// <summary>Cranked and would not fire</summary>
    public bool FailedToStart { get; private set; }

    public BodyPose Pose => _body.Pose;

    /// <summary>Nothing left to show: the engine is off and the body has settled</summary>
    public bool IsAtRest => State == EngineState.Off && _body.IsSettled;

    /// <summary>A pop out of the exhaust on a sharp lift, with how hard the engine was pulling</summary>
    public event Action<float>? Backfired;

    public void Start()
    {
        if (State is EngineState.Cranking or EngineState.Running) return;
        FailedToStart = false;
        Enter(EngineState.Cranking);
    }

    public void Stop()
    {
        if (State is EngineState.Off or EngineState.Stopping) return;
        Enter(EngineState.Stopping);
    }

    /// <summary>Already running, at idle: a car that drives up has had its engine going all along</summary>
    public void StartRunning()
    {
        if (State == EngineState.Running) return;
        FailedToStart = false;
        Rpm = Idle;
        Enter(EngineState.Running);
        _stateTime = CatchFlareSeconds;
    }

    /// <summary>
    /// In gear with the clutch out: the wheels hold the engine at <paramref name="rpm"/>, whatever it makes, and the
    /// throttle only says how hard it pulls. The body leans with that pull. Let go of (a plain <see cref="Tick"/>),
    /// it runs on free from there.
    /// </summary>
    public void TickCoupled(double dt, double rpm, double throttle)
    {
        dt = Math.Clamp(dt, 0, 0.05);
        _time += dt;
        _stateTime += dt;
        if (State != EngineState.Running) StartRunning();

        Rpm = Math.Max(0, rpm);
        _cut = false;
        _load += (Math.Clamp(throttle, 0, 1) - _load) * Math.Min(1, dt / IntakeLag);
        WatchForBackfire(throttle);

        var pull = _spec.TorqueAt(Math.Max(Rpm, 400)) * _load - Losses(Rpm, _load);
        var firing = Idle / Math.Max(Rpm, Idle);
        _body.Tick(dt, _time, pull / PeakTorque, firing, Rpm);
    }

    private void Enter(EngineState state)
    {
        State = state;
        _stateTime = 0;
    }

    /// <param name="pedal">Where the throttle pedal is, 0..1</param>
    public void Tick(double dt, double pedal)
    {
        dt = Math.Clamp(dt, 0, 0.05);
        _time += dt;
        _stateTime += dt;

        var reaction = 0.0;
        switch (State)
        {
            case EngineState.Cranking:
                reaction = Crank(dt);
                break;
            case EngineState.Running:
                reaction = Run(dt, pedal);
                break;
            case EngineState.Stopping:
                reaction = Coast(dt);
                break;
            case EngineState.Off:
                _load = 0;
                Rpm = 0;
                break;
        }

        var firing = State == EngineState.Running ? Idle / Math.Max(Rpm, Idle) : 0;
        _body.Tick(dt, _time, reaction / PeakTorque, firing, Rpm);
    }

    private double Crank(double dt)
    {
        // The starter drags the engine up to speed; each compression pulls it back a little
        var pulse = Math.Sin(_time * Math.PI * 2 * 3.2) * 25;
        Rpm += (CrankRpm - Rpm) * Math.Min(1, dt * 9);
        Rpm = Math.Max(0, Rpm + pulse * dt * 6);
        _load = 0;

        if (_spec.Runs && _stateTime >= CrankSeconds + _random.NextDouble() * 0.35)
        {
            Enter(EngineState.Running);
            return 0;
        }

        if (!_spec.Runs && _stateTime >= CrankNoStartSeconds)
        {
            FailedToStart = true;
            Enter(EngineState.Stopping);
        }

        // The starter itself twists the block a touch
        return 0.08 * PeakTorque * (1 + Math.Sin(_time * Math.PI * 2 * 3.2));
    }

    private double Run(double dt, double pedal)
    {
        // Idle control: just enough air to hold the idle, more when it sags
        var idleLoad = Math.Clamp(IdleHold() + IdleGain * (Idle - Rpm) / Idle, 0, 0.6);

        // Catching: a flare of fuel before it settles
        if (_stateTime < CatchFlareSeconds) idleLoad = Math.Max(idleLoad, CatchFlareLoad * (1 - _stateTime / CatchFlareSeconds));

        var asked = Math.Max(Airflow(pedal), idleLoad);

        // The limiter cuts the spark until the engine drops back
        if (Rpm >= Limiter) _cut = true;
        else if (Rpm < Limiter - LimiterHysteresisRpm) _cut = false;
        if (_cut) _lastCut = _time;

        _load += (asked - _load) * Math.Min(1, dt / IntakeLag);
        WatchForBackfire(pedal);

        var combustion = _cut ? 0 : _spec.TorqueAt(Math.Max(Rpm, 400)) * _load;
        return Spin(dt, combustion);
    }

    private double Coast(double dt)
    {
        _load += (0 - _load) * Math.Min(1, dt / IntakeLag);
        var reaction = Spin(dt, 0);

        // Near the end the last compressions stop it short
        if (Rpm < 300) Rpm = Math.Max(0, Rpm - 900 * dt);
        if (Rpm <= StoppedRpm)
        {
            Rpm = 0;
            _load = 0;
            Enter(EngineState.Off);
        }

        return reaction;
    }

    /// <summary>Speeds the crank up by what is left of the combustion torque; returns that torque, which twists the block</summary>
    private double Spin(double dt, double combustion)
    {
        var omega = Rpm * Math.PI / 30;
        var accelerating = combustion - Losses(Rpm, _load);
        omega = Math.Max(0, omega + accelerating / Inertia * dt);
        Rpm = omega * 30 / Math.PI;
        return accelerating;
    }

    private double Losses(double rpm, double load)
    {
        var x = rpm / Limiter;
        return PeakTorque * (FrictionBase + FrictionSquare * x * x + PumpingLoss * (1 - load) * x);
    }

    /// <summary>
    /// How full the cylinders get with the pedal where it is. A part-open throttle passes about the same air whatever
    /// the speed, so the faster the engine turns the less of it each cylinder gets: out of gear, half a pedal settles
    /// part-way up the rev range instead of running on to the limiter, and only near the floor does it get there.
    /// </summary>
    private double Airflow(double pedal)
    {
        var p = Math.Clamp(pedal, 0, 1);
        if (p <= 0) return 0;

        var opening = 0.12 * p + 0.88 * p * p * p;
        return Math.Min(1, opening / Math.Max(Rpm / Limiter, 0.08));
    }

    /// <summary>The share of the throttle that holds the idle once there</summary>
    private double IdleHold() => Losses(Idle, 0.1) / Math.Max(1, _spec.TorqueAt(Idle));

    // A snap off the throttle from high up pops
    private void WatchForBackfire(double pedal)
    {
        if (pedal > 0.5) _peakLoadSinceLift = Math.Max(_peakLoadSinceLift, _load);
        if (pedal > 0.05 || _peakLoadSinceLift < 0.6) return;

        var pulling = _peakLoadSinceLift;
        _peakLoadSinceLift = 0;
        if (Rpm > Limiter * 0.5 && _random.NextDouble() < 0.7) Backfired?.Invoke((float)pulling);
    }
}

/// <summary>
/// The body on its springs: pushed over by the torque on the block, and shaken by the firing at idle. A spring and
/// a damper per movement, so a kick overshoots and settles like a car does.
/// </summary>
internal sealed class BodyRock
{
    // How far full torque leans the body, and how the springs answer
    private const double RollPerTorque = 1.3 * Math.PI / 180;
    private const double PitchPerRoll = 0.18;
    private const double Frequency = 1.5;
    private const double Damping = 0.28;

    // Shake at idle: a lumpy eight most, a smooth six least
    private const double IdleShake = 0.1 * Math.PI / 180;
    private const double IdleHeave = 0.0015;

    private readonly double _shakeScale;
    private readonly double _phase = Random.Shared.NextDouble() * 10;
    private double _roll;
    private double _rollSpeed;
    private double _shakeRoll;
    private double _shakeHeave;

    public BodyRock(int cylinders)
    {
        _shakeScale = cylinders switch
        {
            <= 4 => 1.3,
            6 => 0.6,
            8 => 1.0,
            _ => 0.7
        };
    }

    public BodyPose Pose { get; private set; } = BodyPose.Rest;

    public bool IsSettled => Math.Abs(_roll) < 2e-5 && Math.Abs(_rollSpeed) < 2e-4 && Math.Abs(_shakeRoll) < 1e-5;

    /// <param name="torque">Torque on the block as a share of the engine's peak</param>
    /// <param name="firing">How strongly the firing shakes the car: 1 at idle, less as it revs, 0 when not running</param>
    public void Tick(double dt, double time, double torque, double firing, double rpm)
    {
        var w = 2 * Math.PI * Frequency;
        var target = RollPerTorque * Math.Clamp(torque, -1.5, 1.5);

        // Small steps: a spring integrated at 60 Hz with a big kick is where it goes unstable
        const int steps = 4;
        var h = dt / steps;
        for (var i = 0; i < steps; i++)
        {
            var acceleration = w * w * (target - _roll) - 2 * Damping * w * _rollSpeed;
            _rollSpeed += acceleration * h;
            _roll += _rollSpeed * h;
        }

        // The cam turns at half the crank: that beat is what shows. Kept under what 60 frames a second can draw
        var beat = Math.Min(rpm / 120, 11) * 2 * Math.PI;
        var shake = firing * firing * _shakeScale;
        var wobble = Math.Sin(beat * time + _phase) * 0.65 + Math.Sin(beat * 1.73 * time + _phase * 2) * 0.35;
        _shakeRoll += (IdleShake * shake * wobble - _shakeRoll) * Math.Min(1, dt * 30);
        _shakeHeave += (IdleHeave * shake * Math.Sin(beat * 0.5 * time + _phase) - _shakeHeave) * Math.Min(1, dt * 30);

        var roll = _roll + _shakeRoll;
        Pose = new BodyPose((float)roll, (float)(_roll * PitchPerRoll), (float)_shakeHeave);
    }
}
