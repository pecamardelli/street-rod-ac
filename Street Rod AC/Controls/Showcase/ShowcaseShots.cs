using System.Numerics;

namespace Street_Rod_AC.Controls.Showcase;

/// <summary>
/// Where an orbit camera is: the point it looks at, how far back it stands, and from which way. The same four
/// figures AcTools' CameraOrbit takes, so a pose goes on the camera as it is.
///
/// Alpha turns round the car: 0 stands off its left side (+X), π/2 in front of it (+Z). Beta lifts the camera over
/// the point it looks at.
/// </summary>
public readonly record struct CameraPose(Vector3 Target, float Radius, float Alpha, float Beta)
{
    /// <summary>Where the camera itself stands, worked out the way CameraOrbit does it</summary>
    public Vector3 Position
    {
        get
        {
            var side = Radius * MathF.Cos(Beta);
            return new Vector3(
                Target.X + side * MathF.Cos(Alpha),
                Target.Y + Radius * MathF.Sin(Beta),
                Target.Z + side * MathF.Sin(Alpha));
        }
    }

    public static CameraPose Lerp(CameraPose a, CameraPose b, float t) => new(
        Vector3.Lerp(a.Target, b.Target, t),
        a.Radius + (b.Radius - a.Radius) * t,
        a.Alpha + (b.Alpha - a.Alpha) * t,
        a.Beta + (b.Beta - a.Beta) * t);
}

/// <summary>
/// The size of the car a shot is framed on, in the car's own space: standing at the origin, nose towards +Z. Taken
/// from where the wheels are, because the renderer's box for a car takes in its shadow planes and comes out a metre
/// or more too long.
/// </summary>
public readonly record struct CarFrame(float FrontAxle, float RearAxle, float HalfTrack, float WheelY, float Height)
{
    // What sticks out past the axles on a car of the fifties to the seventies
    private const float FrontOverhang = 1.0f;
    private const float RearOverhang = 1.3f;

    /// <summary>Something car-sized, for a model whose wheels cannot be found</summary>
    public static CarFrame Nominal { get; } = new(1.4f, -1.4f, 0.78f, 0.33f, 1.35f);

    public float Nose => FrontAxle + FrontOverhang;
    public float Tail => RearAxle - RearOverhang;
    public float Length => Nose - Tail;
    public float MidZ => (Nose + Tail) / 2f;

    /// <summary>
    /// The frame of a car from the middle of its four wheels, and its height. Null for wheels that make no sense (a
    /// model with its dummies missing or all in one place): the caller falls back to <see cref="Nominal"/>.
    /// </summary>
    public static CarFrame? FromWheels(Vector3 leftFront, Vector3 rightFront, Vector3 leftRear, Vector3 rightRear, float height)
    {
        var front = (leftFront.Z + rightFront.Z) / 2f;
        var rear = (leftRear.Z + rightRear.Z) / 2f;
        var halfTrack = (Math.Abs(leftFront.X - rightFront.X) + Math.Abs(leftRear.X - rightRear.X)) / 4f;
        var wheelY = (leftFront.Y + rightFront.Y + leftRear.Y + rightRear.Y) / 4f;

        // A model built facing backwards has its "front" wheels behind: turn it round rather than frame its boot
        if (front < rear) (front, rear) = (rear, front);

        var wheelbase = front - rear;
        if (wheelbase is < 1.5f or > 4.5f || halfTrack is < 0.4f or > 1.2f || wheelY is < 0.1f or > 0.7f) return null;

        return new CarFrame(front, rear, halfTrack, wheelY, height is > 0.8f and < 3f ? height : Nominal.Height);
    }
}

public enum ShotKind
{
    /// <summary>Low off the front three-quarter, drifting in</summary>
    FrontPushIn,

    /// <summary>Alongside, level with the car, the camera running the length of it</summary>
    SideTrack,

    /// <summary>Down by the rear three-quarter, turning round the tail</summary>
    RearLowSweep,

    /// <summary>From higher up, going slowly round</summary>
    HighOrbit,

    /// <summary>Close on a front wheel</summary>
    WheelCloseUp,

    /// <summary>Right down at the nose, sweeping across the grille</summary>
    NoseLow
}

/// <summary>
/// One camera move: from a pose to another, eased, over a few seconds, inside a room whose nearest wall stands
/// <paramref name="WallRadius"/> from the middle
/// </summary>
public sealed record Shot(ShotKind Kind, CameraPose From, CameraPose To, float Duration, float WallRadius)
{
    /// <summary>
    /// Where the camera is <paramref name="seconds"/> into the shot. Mostly a steady drift, the way a film camera
    /// moves, with a little easing either end so a cut never lands on a camera stopping dead. Kept inside the room
    /// on the way too: both ends being inside does not make every pose between them so.
    /// </summary>
    public CameraPose At(float seconds)
    {
        var t = Math.Clamp(seconds / Duration, 0f, 1f);
        var smooth = t * t * (3f - 2f * t);
        return ShowcaseShots.KeepInside(CameraPose.Lerp(From, To, 0.55f * t + 0.45f * smooth), WallRadius);
    }

    /// <summary>
    /// How much the walls pull the camera in, at worst: 1 when they never do, 0.5 when somewhere on the way it stands at
    /// half the distance the shot was framed at. A shot squeezed hard is a close-up nobody framed.
    /// </summary>
    public float Squeeze
    {
        get
        {
            var worst = 1f;
            for (var i = 0; i <= 8; i++)
            {
                var framed = CameraPose.Lerp(From, To, i / 8f);
                var kept = ShowcaseShots.KeepInside(framed, WallRadius);
                if (framed.Radius > 0f) worst = Math.Min(worst, kept.Radius / framed.Radius);
            }

            return worst;
        }
    }
}

/// <summary>
/// The camera moves of the main screen, framed on whatever car stands in the showroom. A handful of kinds, each
/// with a little chance in it, and every one kept inside the room: a camera through a wall renders the back of it.
/// </summary>
public static class ShowcaseShots
{
    public const float ShotSeconds = 7f;

    // How far inside a wall a camera has to stay, and how high over the floor
    private const float WallMargin = 1.5f;
    private const float MinCameraHeight = 0.18f;

    private const float HalfPi = MathF.PI / 2f;

    private static readonly ShotKind[] Kinds = Enum.GetValues<ShotKind>();

    /// <summary>A kind of shot other than the ones just used, so the same move never plays twice running</summary>
    public static ShotKind NextKind(IReadOnlyCollection<ShotKind> recent, Random random)
    {
        var fresh = Kinds.Where(k => !recent.Contains(k)).ToArray();
        var pool = fresh.Length > 0 ? fresh : Kinds;
        return pool[random.Next(pool.Length)];
    }

    /// <summary>
    /// A shot of the given kind on a car standing in the middle of the room, nose to +Z
    /// </summary>
    public static Shot Make(ShotKind kind, CarFrame car, float wallRadius, bool mirror, Random random) =>
        Make(kind, car, CarPlacement.Origin, wallRadius, mirror, random);

    /// <summary>
    /// A shot of the given kind on a car standing where <paramref name="placement"/> puts it. <paramref name="mirror"/>
    /// takes it from the car's other side. The random source only varies it a little: how far, how fast, which way
    /// round.
    /// </summary>
    public static Shot Make(ShotKind kind, CarFrame car, CarPlacement placement, float wallRadius, bool mirror, Random random)
    {
        var l = car.Length;
        var h = car.Height;
        var jitter = (float)(random.NextDouble() * 2.0 - 1.0);
        var duration = ShotSeconds + jitter * 0.8f;

        var (from, to) = kind switch
        {
            ShotKind.FrontPushIn => (
                new CameraPose(new Vector3(0f, 0.45f * h, car.MidZ + 0.15f * l), 1.45f * l, HalfPi - 0.72f - 0.08f * jitter, 0.10f),
                new CameraPose(new Vector3(0f, 0.42f * h, car.MidZ + 0.18f * l), 1.12f * l, HalfPi - 0.55f, 0.07f)),

            ShotKind.SideTrack => (
                new CameraPose(new Vector3(0f, 0.5f * h, car.Tail + 0.3f * l), 1.05f * l, 0.12f, 0.05f),
                new CameraPose(new Vector3(0f, 0.5f * h, car.Nose - 0.3f * l), 1.05f * l, -0.12f, 0.05f)),

            ShotKind.RearLowSweep => (
                new CameraPose(new Vector3(0f, 0.4f * h, car.MidZ - 0.1f * l), 1.3f * l, -HalfPi + 0.95f, 0.035f),
                new CameraPose(new Vector3(0f, 0.4f * h, car.MidZ - 0.1f * l), 1.18f * l, -HalfPi + 0.42f + 0.1f * jitter, 0.05f)),

            ShotKind.HighOrbit => (
                new CameraPose(new Vector3(0f, 0.3f * h, car.MidZ), 1.55f * l, 0.85f, 0.32f),
                new CameraPose(new Vector3(0f, 0.3f * h, car.MidZ), 1.45f * l, 1.6f + 0.15f * jitter, 0.24f)),

            ShotKind.WheelCloseUp => (
                new CameraPose(new Vector3(car.HalfTrack, car.WheelY, car.FrontAxle), 1.75f, 0.22f, 0.07f),
                new CameraPose(new Vector3(car.HalfTrack, car.WheelY, car.FrontAxle), 1.45f, 0.58f + 0.08f * jitter, 0.05f)),

            ShotKind.NoseLow => (
                new CameraPose(new Vector3(0f, 0.4f * h, car.Nose - 0.2f), 2.9f, HalfPi + 0.38f, 0.03f),
                new CameraPose(new Vector3(0f, 0.4f * h, car.Nose - 0.2f), 2.65f, HalfPi - 0.38f, 0.03f)),

            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        // Half the time the other way through the same move, so a sweep is not always left to right
        if (random.Next(2) == 0) (from, to) = (to, from);

        if (mirror)
        {
            from = Mirror(from);
            to = Mirror(to);
        }

        // Framed in the car's own space, then taken to where the car stands; the walls are the room's, so they come last
        from = placement.ToWorld(from);
        to = placement.ToWorld(to);

        return new Shot(kind, KeepInside(from, wallRadius), KeepInside(to, wallRadius), duration, wallRadius);
    }

    /// <summary>The same pose from the car's other side</summary>
    public static CameraPose Mirror(CameraPose pose) => pose with
    {
        Target = pose.Target with { X = -pose.Target.X },
        Alpha = MathF.PI - pose.Alpha
    };

    /// <summary>
    /// Brings a camera in from beyond the walls and up off the floor. It keeps looking at the same point from the same
    /// direction, only nearer: a tighter shot rather than a view of the back of a wall.
    /// </summary>
    public static CameraPose KeepInside(CameraPose pose, float wallRadius)
    {
        var limit = Math.Max(wallRadius - WallMargin, 2f);

        var position = pose.Position;
        var reach = MathF.Sqrt(position.X * position.X + position.Z * position.Z);
        if (reach > limit)
        {
            // Shrink the radius until the camera sits on the limit: solve |target + r·dir| = limit along the ground
            var dir = new Vector2(MathF.Cos(pose.Alpha), MathF.Sin(pose.Alpha)) * MathF.Cos(pose.Beta);
            var t = new Vector2(pose.Target.X, pose.Target.Z);
            var a = dir.LengthSquared();
            var b = 2f * Vector2.Dot(t, dir);
            var c = t.LengthSquared() - limit * limit;
            var disc = b * b - 4f * a * c;
            if (a > 1e-6f && disc >= 0f)
            {
                var r = (-b + MathF.Sqrt(disc)) / (2f * a);
                pose = pose with { Radius = Math.Max(r, 1f) };
            }
        }

        var height = pose.Target.Y + pose.Radius * MathF.Sin(pose.Beta);
        if (height < MinCameraHeight)
        {
            var sin = Math.Clamp((MinCameraHeight - pose.Target.Y) / pose.Radius, -1f, 1f);
            pose = pose with { Beta = MathF.Asin(sin) };
        }

        return pose;
    }
}
