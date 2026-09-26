namespace Street_Rod_AC.Controls.Street;

/// <summary>Where the rival's car is on its way, a moment at a time</summary>
/// <param name="Position">Metres along the lane from the player's car, ahead positive</param>
/// <param name="Speed">Metres a second</param>
/// <param name="Acceleration">Metres a second squared; negative under the brakes</param>
/// <param name="Rpm">The engine's speed while the clutch is in and the wheels turn it; null with the clutch out (it idles on its own)</param>
/// <param name="Throttle">0..1</param>
/// <param name="Braking">The brake lights are on</param>
/// <param name="Done">Stopped alongside, or gone out of sight</param>
public readonly record struct DriveState(float Position, float Speed, float Acceleration, double? Rpm, double Throttle, bool Braking, bool Done);

/// <summary>
/// The rival's car driving up the lane and stopping alongside, or pulling away from it: a function of time, so the
/// same moment always looks the same. Cruises in second gear, brakes to a stop where the street says, and leaves in
/// a hard launch through the gears.
/// </summary>
public sealed class RivalDrive
{
    // Coming up the street: about 27 mph, in second
    private const float CruiseSpeed = 12f;
    private const float CruiseThrottle = 0.28f;
    private const float BrakeDecel = 4.2f;

    // The clutch goes in below this, and the engine drops to its idle
    private const float ClutchSpeed = 3.5f;

    // Leaving: hard off the line, a little less in each gear, never past what a street car does
    private const float LaunchAccel = 5.2f;
    private const float TopSpeed = 38f;
    private const float ShiftSeconds = 0.25f;

    // Overall ratios (gear times final drive) of a four-speed with a 3.55 rear, and the tyre's radius
    private static readonly float[] Ratios = [2.52f * 3.55f, 1.88f * 3.55f, 1.46f * 3.55f, 1.0f * 3.55f];
    private const float TyreRadius = 0.33f;

    private readonly bool _leaving;
    private readonly float _from;
    private readonly float _to;
    private readonly double _idle;
    private readonly double _shiftRpm;

    // Arriving: cruising until here, then on the brakes
    private readonly float _brakeAt;
    private readonly float _cruiseSeconds;
    private readonly float _brakeSeconds;

    private RivalDrive(bool leaving, float from, float to, double idle, double limiter)
    {
        _leaving = leaving;
        _from = from;
        _to = to;
        _idle = idle;
        _shiftRpm = Math.Max(idle * 3, limiter * 0.85);

        var brakeDistance = CruiseSpeed * CruiseSpeed / (2 * BrakeDecel);
        _brakeAt = Math.Max(from, to - brakeDistance);
        _cruiseSeconds = (_brakeAt - from) / CruiseSpeed;
        _brakeSeconds = CruiseSpeed / BrakeDecel;
    }

    /// <summary>Up the lane from <paramref name="from"/> (behind, negative) to a stop at <paramref name="stopAt"/></summary>
    public static RivalDrive Arrive(float from, float stopAt, double idleRpm, double limiterRpm) =>
        new(false, from, stopAt, idleRpm, limiterRpm);

    /// <summary>Away from a standstill at <paramref name="from"/> until past <paramref name="goneAt"/></summary>
    public static RivalDrive Leave(float from, float goneAt, double idleRpm, double limiterRpm) =>
        new(true, from, goneAt, idleRpm, limiterRpm);

    public bool IsLeaving => _leaving;

    /// <summary>How long it takes, start to finish, in seconds</summary>
    public float Duration => _leaving ? LeaveDuration() : _cruiseSeconds + _brakeSeconds;

    public DriveState At(float t) => _leaving ? LeaveAt(t) : ArriveAt(t);

    private DriveState ArriveAt(float t)
    {
        t = Math.Max(t, 0f);
        if (t < _cruiseSeconds)
        {
            return new DriveState(_from + CruiseSpeed * t, CruiseSpeed, 0f, EngineRpm(CruiseSpeed, 1), CruiseThrottle, false, false);
        }

        var b = t - _cruiseSeconds;
        if (b >= _brakeSeconds) return new DriveState(_to, 0f, 0f, null, 0, false, true);

        var speed = CruiseSpeed - BrakeDecel * b;
        var position = _brakeAt + CruiseSpeed * b - BrakeDecel * b * b / 2;
        double? rpm = speed > ClutchSpeed ? EngineRpm(speed, 1) : null;
        return new DriveState(position, speed, -BrakeDecel, rpm, 0, true, false);
    }

    /// <summary>
    /// A launch: the acceleration falls off with speed, and each shift is a quarter of a second off the throttle.
    /// Stepped rather than solved, from the start every time: a few hundred small steps, cheap enough per frame.
    /// </summary>
    private DriveState LeaveAt(float t)
    {
        const float step = 1f / 120f;
        var position = _from;
        var speed = 0f;
        var gear = 0;
        var shifting = 0f;
        var accel = 0f;

        for (var time = 0f; time < t; time += step)
        {
            var dt = Math.Min(step, t - time);
            if (shifting > 0f)
            {
                shifting -= dt;
                accel = 0.3f;
            }
            else
            {
                accel = LaunchAccel * (1f - speed / TopSpeed) * Ratios[gear] / Ratios[0] * 1.6f;
                accel = Math.Min(accel, LaunchAccel);
                if (gear + 1 < Ratios.Length && EngineRpm(speed, gear) >= _shiftRpm)
                {
                    gear++;
                    shifting = ShiftSeconds;
                }
            }

            speed = Math.Min(TopSpeed, speed + accel * dt);
            position += speed * dt;
        }

        var done = position >= _to;
        var rpm = Math.Max(EngineRpm(speed, gear), _idle * 1.6);
        return new DriveState(position, speed, accel, rpm, shifting > 0f ? 0.1 : 1.0, false, done);
    }

    private float LeaveDuration()
    {
        // Found by stepping: a launch of seventy metres takes some six seconds
        var t = 0f;
        while (t < 30f && !LeaveAt(t).Done) t += 0.25f;
        return t;
    }

    /// <summary>What the engine turns at with the wheels at <paramref name="speed"/> in <paramref name="gear"/> (0 first)</summary>
    private double EngineRpm(float speed, int gear) =>
        Math.Max(_idle, speed / (2 * Math.PI * TyreRadius) * 60 * Ratios[gear]);

    /// <summary>How far a wheel turns for a distance travelled, in radians</summary>
    public static float WheelAngle(float distance) => distance / TyreRadius;
}

/// <summary>
/// The body pitching on its springs: squatting as the car pulls away, diving on the brakes, and rocking back when
/// it stops. A spring and a damper, like the engine's rock.
/// </summary>
public sealed class ChassisPitch
{
    // Radians per m/s² of acceleration: a hard stop dips the nose about two and a half degrees
    private const double PitchPerAccel = 0.6 * Math.PI / 180;
    private const double Frequency = 1.3;
    private const double Damping = 0.35;

    private double _pitch;
    private double _speed;

    /// <summary>Nose up positive</summary>
    public float Pitch => (float)_pitch;

    public bool IsSettled => Math.Abs(_pitch) < 1e-5 && Math.Abs(_speed) < 1e-4;

    public void Tick(double dt, double acceleration)
    {
        var w = 2 * Math.PI * Frequency;
        var target = PitchPerAccel * Math.Clamp(acceleration, -9, 9);
        const int steps = 4;
        var h = dt / steps;
        for (var i = 0; i < steps; i++)
        {
            var a = w * w * (target - _pitch) - 2 * Damping * w * _speed;
            _speed += a * h;
            _pitch += _speed * h;
        }
    }

    public void Reset()
    {
        _pitch = 0;
        _speed = 0;
    }
}
