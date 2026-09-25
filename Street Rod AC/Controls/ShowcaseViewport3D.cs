using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AcTools.Render.Kn5Specific.Objects;
using Street_Rod_AC.Controls.Showcase;
using CarSlot = AcTools.Render.Kn5SpecificForward.ForwardKn5ObjectRenderer.CarSlot;
using Sx = SlimDX;

namespace Street_Rod_AC.Controls;

/// <summary>
/// The main screen's backdrop: a few cars standing together in a showroom, filmed in slow camera moves the way a
/// racing game's menus do it. The camera stays with one car at a time for a few shots, dissolving from one shot into
/// the next and on to another of the cars; after a few of them, a fade through black to another room and other cars.
///
/// It only watches. Nothing here takes the mouse, so the menu drawn over it gets every click.
/// </summary>
public class ShowcaseViewport3D : D3DViewportBase
{
    private const int ShotsPerCar = 3;

    // The cars of a room the camera stays with in turn; the rest stand about
    private const int HeroesPerScene = 3;

    // A room whose cars will not load is skipped. This many in a row and something is wrong with more than one room.
    private const int MaxFailedSets = 3;

    // Frames drawn with the new scene before it is faded up: the first ones are still working out shadows and reflections
    private const int SettleFrames = 4;

    /// <summary>
    /// How much car geometry a room will hold. Models run from 11 MB to over 400 MB: past this, the rest of the cars
    /// of the room are left out.
    /// </summary>
    private const long CarByteBudget = 900L * 1024 * 1024;

    private static readonly TimeSpan SceneFadeOut = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan SceneFadeIn = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan Dissolve = TimeSpan.FromMilliseconds(1000);

    private readonly Random _random = new();

    /// <summary>The last frame of the shot before, laid over the new one and faded off it</summary>
    private readonly System.Windows.Controls.Image _still;

    private ShowcasePlaylist? _playlist;

    /// <summary>What should be standing in the scene, and what is</summary>
    private ShowcaseSet? _wanted;
    private ShowcaseSet? _standing;

    private int _failedSets;

    /// <summary>A car standing in the room</summary>
    private sealed record LineupCar(ShowcaseEntry Entry, CarSlot Slot, CarPlacement Placement, CarFrame Frame);

    private readonly List<LineupCar> _lineup = [];
    private readonly List<int> _heroes = [];
    private int _heroTurn;

    private Shot? _shot;
    private float _shotTime;
    private int _shotsTaken;
    private bool _mirror;
    private readonly List<ShotKind> _recent = [];

    /// <summary>The next room has been asked for and the picture is on its way out</summary>
    private bool _leaving;

    /// <summary>Frames still to draw before the scene is faded up, or -1 when there is nothing to fade up</summary>
    private int _fadeInAfter = -1;

    public ShowcaseViewport3D()
        : base("Showcase", new LabelLook(14, 0xD8, 0x90, new Thickness(9, 5, 9, 5), new Vector(18, 18)))
    {
        IsHitTestVisible = false;

        _still = new System.Windows.Controls.Image
        {
            Stretch = Stretch.Fill,
            Opacity = 0,
            IsHitTestVisible = false
        };
        Children.Add(_still);
    }

    protected override string ViewportName => "Main screen viewport";

    #region Dependency properties

    public static readonly DependencyProperty CarsProperty = DependencyProperty.Register(
        nameof(Cars), typeof(IReadOnlyList<ShowcaseCar>), typeof(ShowcaseViewport3D),
        new PropertyMetadata(null, (d, _) => ((ShowcaseViewport3D)d).OnContentChanged()));

    /// <summary>The cars to show, picked from at random</summary>
    public IReadOnlyList<ShowcaseCar>? Cars
    {
        get => (IReadOnlyList<ShowcaseCar>?)GetValue(CarsProperty);
        set => SetValue(CarsProperty, value);
    }

    public static readonly DependencyProperty ScenesProperty = DependencyProperty.Register(
        nameof(Scenes), typeof(IReadOnlyList<ShowcaseScene>), typeof(ShowcaseViewport3D),
        new PropertyMetadata(null, (d, _) => ((ShowcaseViewport3D)d).OnContentChanged()));

    /// <summary>The rooms they are shown in, taken in turn</summary>
    public IReadOnlyList<ShowcaseScene>? Scenes
    {
        get => (IReadOnlyList<ShowcaseScene>?)GetValue(ScenesProperty);
        set => SetValue(ScenesProperty, value);
    }

    private static readonly DependencyPropertyKey CurrentTitlePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(CurrentTitle), typeof(string), typeof(ShowcaseViewport3D), new PropertyMetadata(null));

    public static readonly DependencyProperty CurrentTitleProperty = CurrentTitlePropertyKey.DependencyProperty;

    /// <summary>The name of the car the camera is on, or null while none is</summary>
    public string? CurrentTitle
    {
        get => (string?)GetValue(CurrentTitleProperty);
        private set => SetValue(CurrentTitlePropertyKey, value);
    }

    private static readonly DependencyPropertyKey IsEmptyPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsEmpty), typeof(bool), typeof(ShowcaseViewport3D), new PropertyMetadata(true));

    public static readonly DependencyProperty IsEmptyProperty = IsEmptyPropertyKey.DependencyProperty;

    /// <summary>
    /// There is nothing to show: no cars, no rooms, or cars that will not load. The screen shows its picture instead,
    /// as it does when the viewport has failed.
    /// </summary>
    public bool IsEmpty
    {
        get => (bool)GetValue(IsEmptyProperty);
        private set => SetValue(IsEmptyPropertyKey, value);
    }

    #endregion

    #region Loading

    private void OnContentChanged()
    {
        var cars = Cars ?? [];
        var scenes = Scenes ?? [];

        _playlist = cars.Count > 0 && scenes.Count > 0 ? new ShowcasePlaylist(cars, scenes, _random) : null;
        _wanted = _playlist?.Next();
        _leaving = false;
        _failedSets = 0;
        IsEmpty = _wanted == null;
        RequestLoad();
    }

    protected override bool IsUpToDate => _wanted == null
        ? Renderer == null
        : Renderer != null && _standing == _wanted;

    protected override string LoadDescription => _wanted is { } set
        ? $"{string.Join(", ", set.Cars.Select(c => c.Car.Id))} in {set.Scene.Id}"
        : "";

    protected override async Task LoadAsync()
    {
        if (_wanted is not { } set)
        {
            // Nothing to show: the screen shows its picture instead
            DisposeRenderer(releaseDevice: false);
            return;
        }

        if (Renderer != null && !IsPictureHidden) await FadeOutAsync(SceneFadeOut);

        // Another room is another scene. The D3D9 device the picture goes through stays up for it.
        DisposeRenderer(releaseDevice: false);

        var started = DateTime.Now;
        var renderer = NewRenderer(null, set.Scene.Kn5);

        // The cars stand where this control puts them and are seen from where it puts the camera: nothing else may
        // move them or light them up
        renderer.LoadCarLights = false;
        renderer.TryToGuessCarLights = false;
        renderer.AutoAdjustTarget = false;

        if (!await StartRendererAsync(renderer)) return;

        await PutCarsAsync(renderer, set);
        if (Renderer != renderer || !IsLoaded) return;

        _standing = set;

        if (_lineup.Count == 0)
        {
            // Take the room as standing, empty, and go on to another
            if (++_failedSets >= MaxFailedSets)
            {
                // Failed like a viewport that cannot draw: the screen shows its picture
                Logger.Warning("{Count} rooms in a row had no car that would load; the main screen shows its picture", _failedSets);
                _wanted = null;
                IsEmpty = true;
                Fail();
                return;
            }

            _wanted = _playlist?.Next();
            RequestLoad();
            return;
        }

        _failedSets = 0;
        Logger.Information("Main screen scene {Scene} up with {Cars} cars in {Ms} ms",
            set.Scene.Id, _lineup.Count, (int)(DateTime.Now - started).TotalMilliseconds);

        // The cars the camera stays with, in a random order
        _heroes.Clear();
        _heroes.AddRange(Enumerable.Range(0, _lineup.Count).OrderBy(_ => _random.Next()).Take(HeroesPerScene));
        _heroTurn = 0;
        StartHero();
        _leaving = false;

        renderer.RefreshShadows();
        _fadeInAfter = SettleFrames;
        Invalidate();
    }

    /// <summary>
    /// Stands the cars in the room one after another, where a layout puts them. One at a time on purpose: loading a
    /// car ends with device calls made from a worker thread outside the renderer's lock. A car that will not load, or
    /// would take the room past its budget, is left out (logged).
    /// </summary>
    private async Task PutCarsAsync(GarageRenderer renderer, ShowcaseSet set)
    {
        _lineup.Clear();

        var placements = ShowcaseLineup.Arrange(set.Cars.Count, set.Scene.WallRadius, _random);
        var budget = CarByteBudget;

        for (var i = 0; i < placements.Count && i < set.Cars.Count; i++)
        {
            var entry = set.Cars[i];
            var directory = entry.Car.Directory;
            if (!Directory.Exists(directory))
            {
                Logger.Warning("Car folder not found, not shown: {Directory}", directory);
                continue;
            }

            var bytes = EstimateCarBytes(directory);
            if (bytes > budget && _lineup.Count > 0) continue;
            budget -= bytes;

            var skin = entry.Skin != null && Directory.Exists(Path.Combine(directory, "skins", entry.Skin))
                ? entry.Skin
                : Kn5RenderableCar.DefaultSkin;

            try
            {
                // The first car takes the main slot, which the scene's shadows are worked out from
                var slot = _lineup.Count == 0 ? renderer.MainSlot : renderer.AddCar(null);

                // The slot hands a matrix set while it was empty to the car it is given: set before and after
                Place(slot, placements[i]);
                await TrackDeviceWork(slot.SetCarAsync(CarDescription.FromDirectory(directory), skin));
                if (Renderer != renderer || !IsLoaded) return;

                Place(slot, placements[i]);
                if (slot.CarNode == null) continue;

                _lineup.Add(new LineupCar(entry, slot, placements[i], FrameOf(slot)));
                Invalidate();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not show {Car} on the main screen", entry.Car.Id);
            }
        }
    }

    private static void Place(CarSlot slot, CarPlacement placement)
    {
        slot.LocalMatrix = Sx.Matrix.RotationY(placement.Heading) * Sx.Matrix.Translation(placement.X, 0f, placement.Z);
        slot.ResetCarBoundingBox();
    }

    /// <summary>The car's size, from its wheels; something car-sized when they cannot be found</summary>
    private static CarFrame FrameOf(CarSlot slot)
    {
        var node = slot.CarNode;
        if (node == null) return CarFrame.Nominal;

        // A node's Matrix is where it is in the room, placement included: taken back into the car's own space through
        // the car's own matrix, or every car but one standing in the middle is framed somewhere it is not
        var toCar = Sx.Matrix.Invert(node.Matrix);
        System.Numerics.Vector3? Hub(string name)
        {
            if (node.RootObject.GetDummyByName(name)?.Matrix is not { } m) return null;
            var local = Sx.Vector3.TransformCoordinate(new Sx.Vector3(m.M41, m.M42, m.M43), toCar);
            return new System.Numerics.Vector3(local.X, local.Y, local.Z);
        }

        if (Hub("WHEEL_LF") is not { } lf || Hub("WHEEL_RF") is not { } rf ||
            Hub("WHEEL_LR") is not { } lr || Hub("WHEEL_RR") is not { } rr)
        {
            return CarFrame.Nominal;
        }

        // The box is in the room's space and the car may stand anywhere in it: only its height is wanted
        var height = slot.GetCarBoundingBox()?.Maximum.Y ?? CarFrame.Nominal.Height;
        return CarFrame.FromWheels(lf, rf, lr, rr, height) ?? CarFrame.Nominal;
    }

    /// <summary>Roughly what a car will cost to hold: the biggest model in its folder</summary>
    private static long EstimateCarBytes(string carDirectory)
    {
        try
        {
            return new DirectoryInfo(carDirectory)
                .EnumerateFiles("*.kn5", SearchOption.TopDirectoryOnly)
                .Where(f => !f.Name.Equals("collider.kn5", StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Length)
                .DefaultIfEmpty(0L)
                .Max();
        }
        catch
        {
            return 0L;
        }
    }

    #endregion

    #region Shots

    private LineupCar? Hero => _heroTurn < _heroes.Count && _heroes[_heroTurn] < _lineup.Count ? _lineup[_heroes[_heroTurn]] : null;

    /// <summary>The camera goes to the next of the cars it stays with</summary>
    private void StartHero()
    {
        _shotsTaken = 0;
        _mirror = _random.Next(2) == 0;
        CurrentTitle = Hero?.Entry.Car.Title;
        StartShot();
    }

    private void StartShot()
    {
        if (_standing is not { } set || Hero is not { } hero) return;

        // Now and then from the car's other side
        if (_random.Next(3) == 0) _mirror = !_mirror;

        var others = _lineup.Where(c => c != hero).Select(c => new CarObstacle(c.Placement, c.Frame)).ToList();
        _shot = ShowcaseLineup.Choose(hero.Frame, hero.Placement, set.Scene.WallRadius, others, _recent, _mirror, _random);

        _recent.Add(_shot.Kind);
        if (_recent.Count > 2) _recent.RemoveAt(0);

        _shotTime = 0f;
        Aim(_shot.At(0f));
    }

    private void Aim(CameraPose pose)
    {
        var renderer = Renderer;
        var orbit = renderer?.CameraOrbit;
        if (orbit == null) return;

        orbit.Target = new Sx.Vector3(pose.Target.X, pose.Target.Y, pose.Target.Z);
        orbit.Radius = pose.Radius;
        orbit.Alpha = pose.Alpha;
        orbit.Beta = pose.Beta;
        renderer!.IsDirty = true;
    }

    /// <summary>Cuts to another shot, of the same car or the next, dissolving from the last frame of this one</summary>
    private void CutTo(Action next)
    {
        // A software copy, but a cheap one: about 2 ms at 1080p
        var still = CopyPicture();
        next();

        if (still == null) return;

        _still.Source = still;
        var fade = new DoubleAnimation(1, 0, Dissolve);
        fade.Completed += (_, _) =>
        {
            if (ReferenceEquals(_still.Source, still)) _still.Source = null;
        };
        _still.BeginAnimation(OpacityProperty, fade);
    }

    private void ClearStill()
    {
        _still.BeginAnimation(OpacityProperty, null);
        _still.Opacity = 0;
        _still.Source = null;
    }

    #endregion

    #region Rendering

    protected override bool StepFrame(GarageRenderer renderer, float dt)
    {
        if (_shot is not { } shot || _standing == null) return false;

        _shotTime += dt;

        if (!_leaving)
        {
            var lastOfCar = _shotsTaken + 1 >= ShotsPerCar;
            var lastOfRoom = lastOfCar && _heroTurn + 1 >= _heroes.Count;

            // The room's last shot starts fading out before it ends, so the camera is still moving as it goes dark
            var end = shot.Duration - (lastOfRoom ? (float)SceneFadeOut.TotalSeconds : 0f);
            if (_shotTime >= end)
            {
                if (lastOfRoom)
                {
                    if (_playlist != null)
                    {
                        _leaving = true;
                        _wanted = _playlist.Next();
                        RequestLoad();
                    }
                }
                else if (lastOfCar)
                {
                    CutTo(() =>
                    {
                        _heroTurn++;
                        StartHero();
                    });
                }
                else
                {
                    CutTo(() =>
                    {
                        _shotsTaken++;
                        StartShot();
                    });
                }
            }
        }

        if (_shot != null) Aim(_shot.At(_shotTime));
        return true;
    }

    protected override void OnFramePresented()
    {
        if (_fadeInAfter < 0) return;
        if (_fadeInAfter-- > 0) return;

        if (IsPictureHidden) FadeIn(SceneFadeIn);
    }

    protected override void OnRendererDisposed()
    {
        _standing = null;
        _lineup.Clear();
        _heroes.Clear();
        _shot = null;
        _leaving = false;
        _fadeInAfter = -1;
        CurrentTitle = null;
        ClearStill();
    }

    /// <summary>No engine runs on the main screen</summary>
    protected override void OnPoseChanged()
    {
    }

    #endregion
}
