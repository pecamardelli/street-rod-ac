namespace Street_Rod_AC.Controls.Showcase;

/// <summary>A car the main screen can show: its AC folder, the name it goes by and the skins it comes in</summary>
public sealed record ShowcaseCar(string Id, string Directory, string Title, IReadOnlyList<string> Skins);

/// <summary>
/// A room the cars are shown in. <paramref name="WallRadius"/> is how far from the middle the nearest wall stands,
/// which the camera is kept inside.
/// </summary>
public sealed record ShowcaseScene(string Id, string Kn5, float WallRadius);

/// <summary>A car on show, in one of its skins (null for its default)</summary>
public readonly record struct ShowcaseEntry(ShowcaseCar Car, string? Skin);

/// <summary>A room and the cars standing in it together</summary>
public sealed record ShowcaseSet(ShowcaseScene Scene, IReadOnlyList<ShowcaseEntry> Cars);

/// <summary>
/// What the main screen shows next: a room and a few cars to stand in it. Every car comes round once before any comes
/// round again, no car stands in a room twice, and the room is never the one just left.
/// </summary>
public sealed class ShowcasePlaylist
{
    private readonly IReadOnlyList<ShowcaseCar> _cars;
    private readonly List<ShowcaseScene> _scenes;
    private readonly Random _random;
    private readonly Func<ShowcaseScene, int> _carsFor;

    private readonly Queue<ShowcaseCar> _round = new();
    private ShowcaseCar? _lastCar;
    private ShowcaseScene? _scene;

    /// <param name="carsFor">How many cars a room holds; by default what <see cref="ShowcaseLineup.CarsFor"/> says of its size</param>
    public ShowcasePlaylist(IReadOnlyList<ShowcaseCar> cars, IReadOnlyList<ShowcaseScene> scenes, Random random,
        Func<ShowcaseScene, int>? carsFor = null)
    {
        if (cars.Count == 0) throw new ArgumentException("A playlist needs a car", nameof(cars));
        if (scenes.Count == 0) throw new ArgumentException("A playlist needs a scene", nameof(scenes));

        _cars = cars;
        _scenes = [.. scenes];
        _random = random;
        _carsFor = carsFor ?? (scene => ShowcaseLineup.CarsFor(scene.WallRadius));
    }

    /// <summary>
    /// Takes a room that will not load out of the list, for the rest of the visit. False when that was the last one:
    /// there is nothing left to show then, and <see cref="Next"/> must not be asked.
    /// </summary>
    public bool Drop(ShowcaseScene scene)
    {
        _scenes.Remove(scene);
        return _scenes.Count > 0;
    }

    public ShowcaseSet Next()
    {
        _scene = PickScene(_scene);

        var count = Math.Clamp(_carsFor(_scene), 1, _cars.Count);
        var cars = new List<ShowcaseEntry>(count);
        var guard = 0;
        while (cars.Count < count && guard++ < count * 4)
        {
            var car = PickCar();
            if (cars.Any(c => c.Car == car))
            {
                // The round ran out and started again with a car already here: it waits for the next room
                _round.Enqueue(car);
                continue;
            }

            cars.Add(new ShowcaseEntry(car, PickSkin(car)));
        }

        return new ShowcaseSet(_scene, cars);
    }

    private ShowcaseScene PickScene(ShowcaseScene? previous)
    {
        var choices = _scenes.Where(s => s != previous).ToList();
        if (choices.Count == 0) choices = [.. _scenes];
        return choices[_random.Next(choices.Count)];
    }

    private ShowcaseCar PickCar()
    {
        if (_round.Count == 0)
        {
            // A fresh round in a shuffled order. It must not open with the car the last one closed on.
            var order = _cars.OrderBy(_ => _random.Next()).ToList();
            if (order.Count > 1 && order[0] == _lastCar) (order[0], order[^1]) = (order[^1], order[0]);
            foreach (var car in order) _round.Enqueue(car);
        }

        _lastCar = _round.Dequeue();
        return _lastCar;
    }

    private string? PickSkin(ShowcaseCar car) =>
        car.Skins.Count == 0 ? null : car.Skins[_random.Next(car.Skins.Count)];
}
