using System.Numerics;

namespace Street_Rod_AC.Controls.Showcase;

/// <summary>
/// Where a car stands in the room and which way it faces. <paramref name="Heading"/> is in radians and turns the car
/// the way SlimDX's <c>Matrix.RotationY</c> does, so a placement goes on a car slot as
/// <c>RotationY(Heading) * Translation(X, 0, Z)</c>.
/// </summary>
public readonly record struct CarPlacement(float X, float Z, float Heading)
{
    public static CarPlacement Origin => default;

    /// <summary>A point of the car's own space, where it is in the room</summary>
    public Vector3 ToWorld(Vector3 local)
    {
        var (sin, cos) = MathF.SinCos(Heading);
        return new Vector3(local.X * cos + local.Z * sin + X, local.Y, -local.X * sin + local.Z * cos + Z);
    }

    /// <summary>A camera framed in the car's own space, where it is in the room: the same shot of the car wherever it stands</summary>
    public CameraPose ToWorld(CameraPose pose) => pose with { Target = ToWorld(pose.Target), Alpha = pose.Alpha - Heading };

    /// <summary>A point of the room on the ground, in the car's own space</summary>
    public Vector2 ToLocal(float worldX, float worldZ)
    {
        var (sin, cos) = MathF.SinCos(Heading);
        float dx = worldX - X, dz = worldZ - Z;
        return new Vector2(dx * cos - dz * sin, dx * sin + dz * cos);
    }

    /// <summary>This placement as part of a group that stands at <paramref name="group"/></summary>
    public CarPlacement Within(CarPlacement group)
    {
        var at = group.ToWorld(new Vector3(X, 0f, Z));
        return new CarPlacement(at.X, at.Z, Heading + group.Heading);
    }
}

/// <summary>
/// A car the camera must not stand in or look through: its footprint on the floor, a little wider than its track
/// for the bodywork, and its height
/// </summary>
public readonly record struct CarObstacle(CarPlacement Placement, CarFrame Frame)
{
    // The body sticks out past the tyres' middle by about this much, fenders and mirrors included
    private const float BodyOverhang = 0.32f;

    private float HalfWidth => Frame.HalfTrack + BodyOverhang;

    /// <summary>The point is on the car, or nearer to it than <paramref name="margin"/></summary>
    public bool Contains(Vector3 world, float margin)
    {
        if (world.Y > Frame.Height + margin) return false;
        var p = Placement.ToLocal(world.X, world.Z);
        return Math.Abs(p.X) <= HalfWidth + margin && p.Y >= Frame.Tail - margin && p.Y <= Frame.Nose + margin;
    }

    /// <summary>The line from <paramref name="from"/> to <paramref name="to"/> passes through the car</summary>
    public bool Blocks(Vector3 from, Vector3 to, float margin)
    {
        var a = Placement.ToLocal(from.X, from.Z);
        var b = Placement.ToLocal(to.X, to.Z);
        var d = b - a;

        // Where the line is over the footprint, along its length (0 at the camera, 1 at what it looks at)
        float enter = 0f, leave = 1f;
        if (!Slab(a.X, d.X, -HalfWidth - margin, HalfWidth + margin, ref enter, ref leave)) return false;
        if (!Slab(a.Y, d.Y, Frame.Tail - margin, Frame.Nose + margin, ref enter, ref leave)) return false;

        // Over the footprint, but maybe over the roof too: a camera up high looks down past a car in front
        var low = Math.Min(from.Y + (to.Y - from.Y) * enter, from.Y + (to.Y - from.Y) * leave);
        return low < Frame.Height + margin;
    }

    private static bool Slab(float start, float delta, float min, float max, ref float enter, ref float leave)
    {
        if (Math.Abs(delta) < 1e-6f) return start >= min && start <= max;

        var t1 = (min - start) / delta;
        var t2 = (max - start) / delta;
        if (t1 > t2) (t1, t2) = (t2, t1);
        enter = Math.Max(enter, t1);
        leave = Math.Min(leave, t2);
        return enter <= leave;
    }
}

/// <summary>
/// How a few cars stand together in a room: a row, an echelon of angled bays, or loosely parked. The camera films one
/// of them at a time and keeps clear of the others: a shot that would stand in a car or look through one gives way
/// to another.
/// </summary>
public static class ShowcaseLineup
{
    // Bays: side by side, angled, and loose, for a car of about 5 x 2 m
    private const float RowSpacing = 3.4f;
    private const float EchelonSpacing = 3.8f;
    private const float EchelonStep = 1.3f;
    private const float EchelonAngle = 0.6f;

    private static readonly CarPlacement[] Loose =
    [
        new(0f, 0f, 0f),
        new(-4.6f, -4.9f, 0.45f),
        new(4.9f, -5.7f, -0.5f),
        new(0.6f, -10.6f, 1.45f)
    ];

    // A car's footprint for fitting a layout into a room: half its width and half its length, with room to spare
    private static readonly Vector2 NominalHalfSize = new(1.1f, 2.7f);
    private const float WallClearance = 1.2f;

    // How near a camera may come to a car it is not filming, and to the one it is; how clear its view has to be. Wide:
    // nearer than this the lens clips into the bodywork, and a car next to it fills a corner of a wide frame.
    private const float StandOff = 1.4f;
    private const float HeroStandOff = 0.6f;
    private const float SightMargin = 0.15f;
    private const int Samples = 9;

    // Past this much squeezing by the walls a shot counts as spoilt: a camera pulled in to half its distance
    private const float MinSqueeze = 0.75f;
    private const int SqueezePenalty = Samples;

    /// <summary>How many cars a room holds: three in a shed, four where there is space</summary>
    public static int CarsFor(float wallRadius) => wallRadius >= 20f ? 4 : 3;

    /// <summary>
    /// Where <paramref name="count"/> cars stand in a room: one of the layouts, turned any way and centred, with every
    /// car clear of the walls. Fewer cars when none fits the room; always at least the one in the middle.
    /// </summary>
    public static IReadOnlyList<CarPlacement> Arrange(int count, float wallRadius, Random random)
    {
        for (var n = Math.Max(1, count); n > 1; n--)
        {
            var layouts = new Func<int, List<CarPlacement>>[] { Row, Echelon, LooseLayout }.OrderBy(_ => random.Next()).ToList();
            foreach (var layout in layouts)
            {
                var cars = Centred(layout(n));
                var group = new CarPlacement(0f, 0f, (float)(random.NextDouble() * Math.PI * 2));
                var placed = cars.Select(c => c.Within(group)).ToList();
                if (placed.All(c => Fits(c, wallRadius))) return placed;
            }
        }

        return [CarPlacement.Origin];
    }

    private static List<CarPlacement> Row(int n) =>
        Enumerable.Range(0, n).Select(i => new CarPlacement(i * RowSpacing, 0f, 0f)).ToList();

    private static List<CarPlacement> Echelon(int n) =>
        Enumerable.Range(0, n).Select(i => new CarPlacement(i * EchelonSpacing, -i * EchelonStep, EchelonAngle)).ToList();

    private static List<CarPlacement> LooseLayout(int n) => Loose.Take(n).ToList();

    /// <summary>The group moved so its middle is the middle of the room</summary>
    private static List<CarPlacement> Centred(List<CarPlacement> cars)
    {
        var x = cars.Average(c => c.X);
        var z = cars.Average(c => c.Z);
        return cars.Select(c => c with { X = c.X - x, Z = c.Z - z }).ToList();
    }

    /// <summary>Every corner of the car is clear of the walls</summary>
    public static bool Fits(CarPlacement car, float wallRadius)
    {
        var limit = wallRadius - WallClearance;
        foreach (var sx in new[] { -1f, 1f })
        foreach (var sz in new[] { -1f, 1f })
        {
            var corner = car.ToWorld(new Vector3(sx * NominalHalfSize.X, 0f, sz * NominalHalfSize.Y));
            if (corner.X * corner.X + corner.Z * corner.Z > limit * limit) return false;
        }

        return true;
    }

    /// <summary>
    /// The shot keeps clear of the cars all the way through: the camera never stands in one, the car it films
    /// included, and never looks at its car through another
    /// </summary>
    public static bool IsClear(Shot shot, CarObstacle? hero, IReadOnlyList<CarObstacle> others) => Blocked(shot, hero, others) == 0;

    private static int Blocked(Shot shot, CarObstacle? hero, IReadOnlyList<CarObstacle> others)
    {
        var blocked = 0;
        for (var i = 0; i < Samples; i++)
        {
            var pose = shot.At(shot.Duration * i / (Samples - 1));
            var camera = pose.Position;

            // A wall can pull a camera in until it stands in the very car it films
            if (hero is { } own && own.Contains(camera, HeroStandOff))
            {
                blocked++;
                continue;
            }

            foreach (var other in others)
            {
                if (other.Contains(camera, StandOff) || other.Blocks(camera, pose.Target, SightMargin))
                {
                    blocked++;
                    break;
                }
            }
        }

        return blocked;
    }

    /// <summary>
    /// A shot of the car at <paramref name="placement"/> that keeps clear of the cars and is not squeezed by the walls.
    /// Kinds not used lately come first, from either side of the car; when nothing is clear (a car boxed in), the least
    /// spoilt one. The kind that played last (the last of <paramref name="recent"/>) is never taken again, so the same
    /// move never plays twice running.
    /// </summary>
    public static Shot Choose(CarFrame car, CarPlacement placement, float wallRadius, IReadOnlyList<CarObstacle> others,
        IReadOnlyList<ShotKind> recent, bool mirror, Random random)
    {
        ShotKind? last = recent.Count > 0 ? recent[^1] : null;
        var kinds = Enum.GetValues<ShotKind>()
            .Where(k => k != last)
            .OrderBy(k => recent.Contains(k) ? 1 : 0)
            .ThenBy(_ => random.Next())
            .ToList();

        Shot? best = null;
        var bestBlocked = int.MaxValue;
        foreach (var kind in kinds)
        foreach (var side in new[] { mirror, !mirror })
        {
            var shot = ShowcaseShots.Make(kind, car, placement, wallRadius, side, random);
            var blocked = Blocked(shot, new CarObstacle(placement, car), others)
                          + (shot.Squeeze < MinSqueeze ? SqueezePenalty : 0);
            if (blocked == 0) return shot;
            if (blocked < bestBlocked)
            {
                best = shot;
                bestBlocked = blocked;
            }
        }

        return best!;
    }
}
