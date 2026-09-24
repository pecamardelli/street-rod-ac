using System.Text.RegularExpressions;
using AcTools.Render.Base.Objects;
using AcTools.Render.Kn5Specific.Objects;
using Street_Rod_AC.Audio;
using Sx = SlimDX;

namespace Street_Rod_AC.Controls;

/// <summary>
/// Rocks a car's body on its wheels: the body leans with the engine's torque while the wheels, hubs and suspension
/// stay planted. The whole car is turned, and each wheel group turned back by the same amount, so the model needs no
/// knowledge of which mesh is the body.
///
/// Lean is about the car's long axis at roughly the height of the roll centre, between the wheels; the driver's side
/// lifts on a blip, as it does with an engine that turns clockwise seen from the front.
/// </summary>
internal sealed class CarBodyRock
{
    private const float RollCentreHeight = 0.45f;

    private static readonly Regex WheelGroup = new("^(WHEEL|SUSP|HUB|DISC)_(LF|RF|LR|RR)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Kn5RenderableCar _car;
    private readonly Sx.Matrix _base;
    private readonly List<Held> _held = new();
    private readonly Sx.Vector3 _pivot;
    private readonly float _leftSign;
    private readonly float _frontSign;
    private BodyPose _pose = BodyPose.Rest;

    /// <summary>The lean in world space, for whatever sits in the car without being part of it (the parts view)</summary>
    public Sx.Matrix WorldLean { get; private set; } = Sx.Matrix.Identity;

    /// <summary>A wheel group as it was, and the way from its parent to the car's own space</summary>
    private sealed record Held(RenderableList Node, Sx.Matrix Local, Sx.Matrix ToCar, Sx.Matrix FromCar);

    public CarBodyRock(Kn5RenderableCar car)
    {
        _car = car;
        _base = car.LocalMatrix;

        var toWorld = car.Matrix;
        var fromWorld = Sx.Matrix.Invert(toWorld);
        Collect(car, n =>
        {
            var toCar = n.ParentMatrix * fromWorld;
            _held.Add(new Held(n, n.LocalMatrix, toCar, Sx.Matrix.Invert(toCar)));
        });

        // Where the wheels are says which way is left and which way is the front
        var hubs = _held.Where(h => h.Node is Kn5RenderableList { OriginalNode.Name: var name } && name.StartsWith("WHEEL_", StringComparison.OrdinalIgnoreCase))
            .GroupBy(h => ((Kn5RenderableList)h.Node).OriginalNode.Name.ToUpperInvariant()[6..])
            .ToDictionary(g => g.Key, g => Position(g.First().Local * g.First().ToCar));

        if (hubs.TryGetValue("LF", out var lf) && hubs.TryGetValue("RR", out var rr))
        {
            _leftSign = lf.X >= rr.X ? 1f : -1f;
            _frontSign = lf.Z >= rr.Z ? 1f : -1f;
            _pivot = new Sx.Vector3((lf.X + rr.X) / 2f, RollCentreHeight, (lf.Z + rr.Z) / 2f);
        }
        else
        {
            _leftSign = 1f;
            _frontSign = 1f;
            _pivot = new Sx.Vector3(0f, RollCentreHeight, 0f);
        }
    }

    public Kn5RenderableCar Car => _car;

    private static Sx.Vector3 Position(Sx.Matrix m) => new(m.M41, m.M42, m.M43);

    private static void Collect(RenderableList list, Action<RenderableList> found)
    {
        foreach (var child in list)
        {
            if (child is not RenderableList node) continue;
            if (node is Kn5RenderableList { OriginalNode.Name: var name } && WheelGroup.IsMatch(name))
            {
                found(node);
                continue;
            }

            Collect(node, found);
        }
    }

    /// <summary>Puts the body where the pose says; false when nothing changed</summary>
    public bool Apply(BodyPose pose)
    {
        if (pose == _pose) return false;
        _pose = pose;

        var lean = Sx.Matrix.Translation(-_pivot)
                   * Sx.Matrix.RotationZ(pose.Roll * _leftSign)
                   * Sx.Matrix.RotationX(-pose.Pitch * _frontSign)
                   * Sx.Matrix.Translation(_pivot)
                   * Sx.Matrix.Translation(0f, pose.Heave, 0f);
        var back = Sx.Matrix.Invert(lean);

        _car.LocalMatrix = lean * _base;
        WorldLean = Sx.Matrix.Invert(_base) * lean * _base;
        foreach (var held in _held) held.Node.LocalMatrix = held.Local * held.ToCar * back * held.FromCar;
        return true;
    }

    /// <summary>Everything back where it was</summary>
    public void Release()
    {
        _car.LocalMatrix = _base;
        foreach (var held in _held) held.Node.LocalMatrix = held.Local;
        _pose = BodyPose.Rest;
        WorldLean = Sx.Matrix.Identity;
    }
}
