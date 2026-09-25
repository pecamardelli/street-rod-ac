using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AcTools.Render.Kn5Specific.Objects;
using AcTools.Render.Kn5SpecificForward;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Services.Catalog;
using CarSlot = AcTools.Render.Kn5SpecificForward.ForwardKn5ObjectRenderer.CarSlot;
using Sx = SlimDX;

namespace Street_Rod_AC.Controls;

/// <summary>Where one car stands on a lot, and what to render there</summary>
public readonly record struct LotCarPlacement(
    string CarDirectory,
    string? SkinId,
    float X,
    float Z,
    float Heading,
    string Label);

/// <summary>
/// A dealer's lot: several cars parked in a showroom, seen from a camera that either takes in the whole lot
/// or sits on one car.
///
/// The renderer is the same one the garage uses. It holds a slot per car and draws them all in one scene, so
/// the work here is placing them, moving the camera between the two views, and working out which car the
/// pointer is on.
/// </summary>
public class DealerLotViewport3D : D3DViewportBase
{
    // Taking in the whole lot, and standing on one car
    private const float DefaultLotRadius = 19f;
    private const float LotBeta = 0.30f;
    private const float LotEyeHeight = 1.0f;
    private const float CarRadius = 6.2f;
    private const float CarBeta = 0.14f;

    // What the wheel is allowed to do, either side of where the view sits
    private const float LotZoomIn = 0.45f;
    private const float LotZoomOut = 1.35f;
    private const float MinCarRadius = 3.4f;
    private const float MaxCarRadius = 11f;

    /// <summary>
    /// How quickly the camera settles, as the share of the remaining distance it covers per second. Low
    /// enough to read as a move rather than a cut; an ease either end stops it starting or stopping abruptly.
    /// </summary>
    private const float CameraSettleRate = 2.6f;

    /// <summary>Near enough to the mark to stop working at it</summary>
    private const float CameraSettled = 0.004f;

    private const float MinBeta = 0.02f;
    private const float MaxBeta = 1.1f;

    /// <summary>
    /// Added to every bay's heading. AC car models are not all built facing the same way; if a lot comes out
    /// with every car pointing the same wrong direction, this is the knob, not the bay data.
    /// </summary>
    private const float ModelHeadingOffset = 0f;

    /// <summary>
    /// How much car geometry the lot will hold. Models run from 11 MB to over 400 MB, so a fixed number of
    /// cars is no measure of anything: past this, the rest of the stock stays in the side list only.
    /// </summary>
    private const long CarByteBudget = 1_200L * 1024 * 1024;

    // A car standing in front of you is roughly this big, used to click on one before its real box is known
    private static readonly Sx.Vector3 NominalCarSize = new(2.2f, 1.6f, 5.4f);

    private const double ClickSlack = 4.0;
    private static readonly TimeSpan HoverInterval = TimeSpan.FromMilliseconds(40);

    /// <summary>Slot per car, in the same order as <see cref="Cars"/>. Null where a car was not loaded</summary>
    private readonly List<CarSlot?> _slots = [];
    private IReadOnlyList<LotCarPlacement> _loaded = [];

    /// <summary>
    /// What is actually standing in each bay, as opposed to what is meant to be. The two differ while a lot
    /// is being restocked, and for good on a car that could not be loaded. Anything that reads the scene -
    /// clicking, hovering, framing a car - goes by this one.
    /// </summary>
    private readonly List<LotCarPlacement?> _standing = [];

    /// <summary>The showroom the standing scene was built from: a new one means starting the scene again</summary>
    private string? _loadedShowroom;

    private System.Windows.Point _lastMouse;
    private System.Windows.Point _pressedAt;
    private bool _isDragging;
    private DateTime _lastHoverPick = DateTime.MinValue;
    private int _hovered = -1;

    // Where the camera is going. Alpha is only steered until the player takes hold of it
    private Sx.Vector3 _targetGoal = new(0f, LotEyeHeight, 0f);
    private float _radiusGoal = DefaultLotRadius;
    private float _betaGoal = LotBeta;
    private float? _alphaGoal;

    public DealerLotViewport3D()
        : base("DealerLot", new LabelLook(14, 0xD8, 0x90, new Thickness(9, 5, 9, 5), new Vector(18, 18)))
    {
    }

    protected override string ViewportName => "Dealer lot viewport";

    /// <summary>A car on the lot was clicked, by its index in <see cref="Cars"/></summary>
    public event Action<int>? CarClicked;

    /// <summary>A click that landed on no car</summary>
    public event Action? BackgroundClicked;

    #region Dependency properties

    public static readonly DependencyProperty ShowroomKn5Property = DependencyProperty.Register(
        nameof(ShowroomKn5), typeof(string), typeof(DealerLotViewport3D),
        new PropertyMetadata(null, (d, _) => ((DealerLotViewport3D)d).RequestLoad()));

    /// <summary>Full path to the showroom .kn5 the lot stands in</summary>
    public string? ShowroomKn5
    {
        get => (string?)GetValue(ShowroomKn5Property);
        set => SetValue(ShowroomKn5Property, value);
    }

    public static readonly DependencyProperty CarsProperty = DependencyProperty.Register(
        nameof(Cars), typeof(IReadOnlyList<LotCarPlacement>), typeof(DealerLotViewport3D),
        new PropertyMetadata(null, (d, _) => ((DealerLotViewport3D)d).RequestLoad()));

    /// <summary>The cars standing on the lot, each with the bay it stands in</summary>
    public IReadOnlyList<LotCarPlacement>? Cars
    {
        get => (IReadOnlyList<LotCarPlacement>?)GetValue(CarsProperty);
        set => SetValue(CarsProperty, value);
    }

    public static readonly DependencyProperty LotRadiusProperty = DependencyProperty.Register(
        nameof(LotRadius), typeof(double), typeof(DealerLotViewport3D),
        new PropertyMetadata((double)DefaultLotRadius, (d, _) => ((DealerLotViewport3D)d).OnLotRadiusChanged()));

    /// <summary>
    /// How far back to stand to take the whole lot in. Worked out from the room the lot is in, because a
    /// distance that frames a yard puts the camera through the wall of a shed.
    /// </summary>
    public double LotRadius
    {
        get => (double)GetValue(LotRadiusProperty);
        set => SetValue(LotRadiusProperty, value);
    }

    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex), typeof(int), typeof(DealerLotViewport3D),
        new PropertyMetadata(-1, (d, _) => ((DealerLotViewport3D)d).OnSelectionChanged()));

    /// <summary>Which car the camera sits on, or -1 to take in the whole lot</summary>
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    private static readonly DependencyPropertyKey IsLotReadyPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsLotReady), typeof(bool), typeof(DealerLotViewport3D), new PropertyMetadata(false));

    public static readonly DependencyProperty IsLotReadyProperty = IsLotReadyPropertyKey.DependencyProperty;

    /// <summary>
    /// Every car has been dealt with, one way or another, and the lot is worth looking at. Not the same as
    /// <see cref="IsReady"/>, which is only the first frame: at that point the room is still empty.
    /// </summary>
    public bool IsLotReady
    {
        get => (bool)GetValue(IsLotReadyProperty);
        private set => SetValue(IsLotReadyPropertyKey, value);
    }

    private static readonly DependencyPropertyKey LoadedCountPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(LoadedCount), typeof(int), typeof(DealerLotViewport3D), new PropertyMetadata(0));

    public static readonly DependencyProperty LoadedCountProperty = LoadedCountPropertyKey.DependencyProperty;

    /// <summary>
    /// How many cars are standing on the lot so far. Only the viewport knows: the budget may not have
    /// stretched to all of them.
    /// </summary>
    public int LoadedCount
    {
        get => (int)GetValue(LoadedCountProperty);
        private set => SetValue(LoadedCountPropertyKey, value);
    }

    #endregion

    #region Loading

    protected override bool IsUpToDate => Renderer != null && SameCars(Cars ?? [], _loaded);

    protected override Task LoadAsync() => LoadLotAsync(Cars ?? []);

    private static bool SameCars(IReadOnlyList<LotCarPlacement> a, IReadOnlyList<LotCarPlacement> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (!a[i].Equals(b[i])) return false;
        }
        return true;
    }

    private async Task LoadLotAsync(IReadOnlyList<LotCarPlacement> cars)
    {
        var showroom = ShowroomKn5;
        if (showroom != null && !File.Exists(showroom))
        {
            Logger.Warning("Showroom not found, rendering the lot without it: {Showroom}", showroom);
            showroom = null;
        }

        if (showroom == null && cars.Count == 0)
        {
            // Leaving the screen clears the bindings one at a time, so this is the ordinary way a lot ends.
            // It is not a failure and must not be reported as one: doing so marked the viewport failed and
            // tore the bridge down on the way out of every single dealer.
            DisposeRenderer(releaseDevice: false);
            return;
        }

        // Selling a car changes the stock but not the place it is sold in. Rebuilding the scene would mean
        // reading a showroom of a few hundred MB again for nothing, so only the cars are swapped.
        if (Renderer != null && showroom == _loadedShowroom)
        {
            await UpdateCarsAsync(cars);
            return;
        }

        // A new showroom is a new scene; the D3D9 device the picture goes through stays up for it
        DisposeRenderer(releaseDevice: false);

        var started = DateTime.Now;

        // The cars go in one at a time afterwards, so the scene starts as the empty showroom
        var renderer = NewRenderer(null, showroom);

        // Every car would otherwise bring its own headlights and tail lights along. A lot full of them runs
        // into the shader's own ceiling on how many lights a scene may have, for lamps nobody is looking at.
        // The ambient cubemap is left alone: the cars are meant to sit in the same light as in the garage,
        // and its cost is one small probe per car, one of them refreshed per frame. If a full lot turns out
        // to be slow, CubemapAmbient = 0 switches those probes off and is the first thing to try.
        renderer.LoadCarLights = false;
        renderer.TryToGuessCarLights = false;

        // The camera is driven from here; the renderer must not glide it onto a car of its own accord
        renderer.AutoAdjustTarget = false;

        // The showroom takes seconds to read, and the screen may be gone by now
        if (!await StartRendererAsync(renderer)) return;

        _loadedShowroom = showroom;
        _loaded = cars;
        _slots.Clear();
        LoadedCount = 0;
        IsLotReady = false;

        ApplyLotCamera(immediate: true);
        if (SelectedIndex >= 0) OnSelectionChanged();

        Logger.Information("Lot showroom up in {Ms} ms, {Count} cars to place",
            (int)(DateTime.Now - started).TotalMilliseconds, cars.Count);

        await LoadCarsAsync(cars);
    }

    /// <summary>
    /// Swaps the cars on a lot that is already standing. A slot whose car has not changed is left alone, so
    /// selling one car off a lot of six costs the loading of the ones that shuffled up, not of the whole place.
    /// </summary>
    private async Task UpdateCarsAsync(IReadOnlyList<LotCarPlacement> cars)
    {
        var renderer = Renderer;
        if (renderer == null) return;

        // A bay is about to be told where it stands again: the car in it cannot be leaning when it is
        ReleaseRock();

        var previous = _loaded;
        _loaded = cars;
        // Restocking after a sale moves several cars at once. Cover the lot for that too, rather than let
        // the player watch cars blink out and back in around the one they just bought.
        IsLotReady = false;

        var budget = CarByteBudget;

        for (var i = 0; i < cars.Count; i++)
        {
            if (Renderer != renderer || !IsLoaded) return;

            var car = cars[i];
            if (i < previous.Count && i < _standing.Count && _standing[i] is { } already && already.Equals(car))
            {
                // This bay already holds exactly this car
                budget -= CarModelFiles.EstimateBytes(car.CarDirectory);
                continue;
            }

            while (_slots.Count <= i) _slots.Add(null);
            while (_standing.Count <= i) _standing.Add(null);

            // Whatever was here is not what belongs here. Take it off before anything else: leaving it would
            // mean the old car standing under the new car's name, and a click on it buying the wrong car.
            await ClearBayAsync(i, renderer);
            if (Renderer != renderer || !IsLoaded) return;

            if (!Directory.Exists(car.CarDirectory))
            {
                Logger.Warning("Car folder not found, left off the lot: {Directory}", car.CarDirectory);
                continue;
            }

            var bytes = CarModelFiles.EstimateBytes(car.CarDirectory);
            if (bytes > budget) continue;
            budget -= bytes;

            try
            {
                // Reuse this index's own slot; failing that take the main one if nothing has claimed it yet
                var slot = _slots[i] ?? (_slots.Contains(renderer.MainSlot)
                    ? renderer.AddCar(null)
                    : renderer.MainSlot);

                // Once before the car arrives and once after. The slot keeps a matrix set while it was empty
                // and gives it to the car on arrival, so without the first call the car would appear for a
                // frame wherever the slot was last told to be; without the second it would stay there.
                Place(slot, car);
                await TrackDeviceWork(slot.SetCarAsync(CarDescription.FromDirectory(car.CarDirectory),
                    car.SkinId ?? Kn5RenderableCar.DefaultSkin));

                if (Renderer != renderer || !IsLoaded) return;

                Place(slot, car);
                _slots[i] = slot;
                _standing[i] = car;
                Invalidate();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not put {Car} on the lot", Path.GetFileName(car.CarDirectory));
            }
        }

        // Anything the lot no longer sells is taken off it
        for (var i = cars.Count; i < _slots.Count; i++)
        {
            await ClearBayAsync(i, renderer);
            if (Renderer != renderer || !IsLoaded) return;
        }

        LoadedCount = _standing.Count(c => c != null);
        renderer.RefreshShadows();
        IsLotReady = true;
        Invalidate();

        Logger.Information("Lot restocked: {Loaded} of {Total} cars out front", LoadedCount, cars.Count);
    }

    /// <summary>
    /// Empties a bay: the car goes, and the slot stays behind for the next car put in that bay. The renderer
    /// has no way to take a slot out of the scene again, so handing one back would mean a fresh slot for
    /// every restock and the empty ones piling up in the renderer. The main slot is the one exception: the
    /// scene's shadows hang off it, so it is forgotten here and picked up by whichever bay next needs a slot,
    /// rather than left standing empty in a bay nothing is sold from.
    /// </summary>
    private async Task ClearBayAsync(int index, GarageRenderer renderer)
    {
        if (index >= _slots.Count || _slots[index] is not { } slot) return;

        try
        {
            await TrackDeviceWork(slot.SetCarAsync(null));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not clear a car off the lot");
        }

        if (Renderer != renderer) return;

        if (ReferenceEquals(slot, renderer.MainSlot)) _slots[index] = null;
        if (index < _standing.Count) _standing[index] = null;
    }

    /// <summary>
    /// Puts the cars on the lot one after another.
    ///
    /// One at a time on purpose: loading a car ends with real device calls made from a worker thread without
    /// taking the renderer's lock, so two at once race each other and the drawing. Reading the .kn5 off disk
    /// is the slow part and that is already off this thread.
    /// </summary>
    private async Task LoadCarsAsync(IReadOnlyList<LotCarPlacement> cars)
    {
        // The slot the scene is built around: shadows and reflections are worked out from it, so give it the
        // car standing nearest the middle of the lot
        var main = MostCentralIndex(cars);
        var budget = CarByteBudget;

        for (var i = 0; i < cars.Count; i++)
        {
            _slots.Add(null);
            _standing.Add(null);
        }

        for (var i = 0; i < cars.Count; i++)
        {
            var renderer = Renderer;
            if (renderer == null || !IsLoaded) return;

            var car = cars[i];
            if (!Directory.Exists(car.CarDirectory))
            {
                Logger.Warning("Car folder not found, left off the lot: {Directory}", car.CarDirectory);
                continue;
            }

            var bytes = CarModelFiles.EstimateBytes(car.CarDirectory);
            if (bytes > budget)
            {
                Logger.Information("Lot is full at {Count} cars; {Car} ({Mb} MB) stays in the list only",
                    LoadedCount, Path.GetFileName(car.CarDirectory), bytes / (1024 * 1024));
                continue;
            }
            budget -= bytes;

            try
            {
                var slot = i == main ? renderer.MainSlot : renderer.AddCar(null);

                // Before and after: see the note in UpdateCarsAsync
                Place(slot, car);
                await TrackDeviceWork(slot.SetCarAsync(CarDescription.FromDirectory(car.CarDirectory),
                    car.SkinId ?? Kn5RenderableCar.DefaultSkin));

                if (Renderer != renderer || !IsLoaded) return;

                Place(slot, car);
                _slots[i] = slot;
                _standing[i] = car;
                LoadedCount++;

                Invalidate();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not put {Car} on the lot", Path.GetFileName(car.CarDirectory));
            }
        }

        // The renderer only watches its main slot, so nothing has asked for the shadows of the cars in any
        // of the others. Without this the last car to arrive stands there without one.
        if (Renderer is { } loaded)
        {
            loaded.RefreshShadows();
            IsLotReady = true;
            Invalidate();
        }

        Logger.Information("Lot loaded: {Loaded} of {Total} cars", LoadedCount, cars.Count);
    }

    /// <summary>
    /// Stands a car in its bay.
    ///
    /// The matrix has to go on after the car is loaded, not before: a slot holds a matrix set while it was
    /// empty and hands that same stale one to the next car put in it.
    /// </summary>
    private static void Place(CarSlot slot, LotCarPlacement car)
    {
        var yaw = (car.Heading + ModelHeadingOffset) * (float)Math.PI / 180f;
        slot.LocalMatrix = Sx.Matrix.RotationY(yaw) * Sx.Matrix.Translation(car.X, 0f, car.Z);

        // The box is remembered once asked for, and the car has just moved
        slot.ResetCarBoundingBox();
    }

    private static int MostCentralIndex(IReadOnlyList<LotCarPlacement> cars)
    {
        var best = 0;
        var bestDistance = float.MaxValue;

        for (var i = 0; i < cars.Count; i++)
        {
            var distance = cars[i].X * cars[i].X + cars[i].Z * cars[i].Z;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    #endregion

    #region Camera

    private void OnLotRadiusChanged()
    {
        if (Renderer == null || IsCarView) return;
        ApplyLotCamera(immediate: false);
    }

    private void OnSelectionChanged()
    {
        ReleaseRock();
        if (Renderer == null) return;

        var index = SelectedIndex;
        if (index < 0 || index >= _loaded.Count)
        {
            ApplyLotCamera(immediate: false);
            return;
        }

        var car = _loaded[index];
        var slot = index < _slots.Count ? _slots[index] : null;

        // Stand where the car can be seen whole: its own box if it has one, else where its bay is
        var center = slot?.GetCarBoundingBox() is { } box
            ? (box.Minimum + box.Maximum) / 2f
            : new Sx.Vector3(car.X, NominalCarSize.Y / 2f, car.Z);

        _targetGoal = center;
        _radiusGoal = CarRadius;
        _betaGoal = CarBeta;

        // Come round to its front three-quarter, until the player takes the camera over
        _alphaGoal = (car.Heading + ModelHeadingOffset + 140f) * (float)Math.PI / 180f;

        Invalidate();
    }

    private void ApplyLotCamera(bool immediate)
    {
        _targetGoal = new Sx.Vector3(0f, LotEyeHeight, 0f);
        _radiusGoal = (float)LotRadius;
        _betaGoal = LotBeta;
        _alphaGoal = null;

        var orbit = Renderer?.CameraOrbit;
        if (orbit == null) return;

        if (immediate)
        {
            orbit.Target = _targetGoal;
            orbit.Radius = _radiusGoal;
            orbit.Beta = _betaGoal;
            orbit.Alpha = 0.9f;
        }

        Invalidate();
    }

    /// <summary>
    /// Eases the camera toward wherever it is meant to be. Returns true while it is still moving, so the
    /// render loop knows to keep drawing.
    /// </summary>
    private bool StepCamera(float dt)
    {
        var orbit = Renderer?.CameraOrbit;
        if (orbit == null) return false;

        // A share of what is left, every second: the move starts quickly and settles rather than stopping
        // dead. Worked out per second so it comes out the same whatever the frame rate.
        var k = 1f - (float)Math.Exp(-CameraSettleRate * dt);
        var moving = false;

        var target = orbit.Target;
        var toTarget = _targetGoal - target;
        if (toTarget.LengthSquared() > CameraSettled * CameraSettled)
        {
            orbit.Target = target + toTarget * k;
            moving = true;
        }
        else
        {
            orbit.Target = _targetGoal;
        }

        if (Math.Abs(_radiusGoal - orbit.Radius) > CameraSettled)
        {
            orbit.Radius += (_radiusGoal - orbit.Radius) * k;
            moving = true;
        }

        if (Math.Abs(_betaGoal - orbit.Beta) > CameraSettled)
        {
            orbit.Beta += (_betaGoal - orbit.Beta) * k;
            moving = true;
        }

        if (_alphaGoal is { } alphaGoal)
        {
            // Round the short way
            var delta = (float)Math.IEEERemainder(alphaGoal - orbit.Alpha, Math.PI * 2);
            if (Math.Abs(delta) > CameraSettled)
            {
                orbit.Alpha += delta * k;
                moving = true;
            }
            else
            {
                _alphaGoal = null;
            }
        }

        return moving;
    }

    private bool IsCarView => SelectedIndex >= 0 && SelectedIndex < _loaded.Count;

    #endregion

    #region Picking

    /// <summary>
    /// Which car is under a point of the viewport, or -1 for none.
    ///
    /// One box per car and no triangles: a car's own node never reports a hit anyway, and for picking a whole
    /// car out of a lot its box is both cheaper and kinder to aim at.
    /// </summary>
    private int PickAt(System.Windows.Point position)
    {
        var renderer = Renderer;
        var camera = renderer?.Camera;
        if (renderer == null || camera == null || ActualWidth <= 0 || ActualHeight <= 0) return -1;

        var ray = camera.GetPickingRay(
            new Sx.Vector2((float)position.X, (float)position.Y),
            new Sx.Vector2((float)ActualWidth, (float)ActualHeight));

        var best = -1;
        var bestDistance = float.MaxValue;

        for (var i = 0; i < _slots.Count && i < _loaded.Count; i++)
        {
            var box = BoxOf(i);
            if (box == null) continue;

            if (Sx.Ray.Intersects(ray, box.Value, out float distance) && distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    private Sx.BoundingBox? BoxOf(int index)
    {
        var slot = _slots[index];
        if (slot == null) return null;

        // A bay that does not yet hold the car it is meant to hold is not there to be clicked: an empty patch
        // of floor must not answer for a car, and nor must the car that has not been taken off it yet
        if (index >= _standing.Count || _standing[index] is not { } standing || !standing.Equals(_loaded[index]))
            return null;

        if (slot.GetCarBoundingBox() is { } box) return box;

        // Loaded but not measured yet: a car-sized box where the bay is will do
        var car = _loaded[index];
        var half = NominalCarSize / 2f;
        var center = new Sx.Vector3(car.X, half.Y, car.Z);
        return new Sx.BoundingBox(center - half, center + half);
    }

    private void UpdateHover(System.Windows.Point position)
    {
        var now = DateTime.Now;
        if (now - _lastHoverPick < HoverInterval) return;
        _lastHoverPick = now;

        // On one car there is nothing to pick out: the whole viewport is that car
        var hit = IsCarView ? -1 : PickAt(position);
        if (hit != _hovered)
        {
            _hovered = hit;
            Cursor = hit >= 0 ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;
        }

        ShowLabel(hit < 0 ? null : _loaded[hit].Label, position);
    }

    #endregion

    #region Input

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _pressedAt = e.GetPosition(this);
        _lastMouse = _pressedAt;
        _isDragging = true;
        CaptureMouse();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_isDragging) return;

        _isDragging = false;
        ReleaseMouseCapture();

        var position = e.GetPosition(this);
        var travelled = Math.Abs(position.X - _pressedAt.X) + Math.Abs(position.Y - _pressedAt.Y);
        if (travelled > ClickSlack) return;

        var hit = IsCarView ? -1 : PickAt(position);

        // Set it here rather than leaving it to the host: a two-way binding carries it back to the screen,
        // and the camera starts moving on the same frame as the click
        SetCurrentValue(SelectedIndexProperty, hit);

        if (hit >= 0)
        {
            CarClicked?.Invoke(hit);
        }
        else
        {
            BackgroundClicked?.Invoke();
        }
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var position = e.GetPosition(this);

        if (!_isDragging || e.LeftButton != MouseButtonState.Pressed)
        {
            UpdateHover(position);
            return;
        }

        var orbit = Renderer?.CameraOrbit;
        if (orbit == null) return;

        var dx = position.X - _lastMouse.X;
        var dy = position.Y - _lastMouse.Y;
        _lastMouse = position;

        if (Math.Abs(dx) < 0.01 && Math.Abs(dy) < 0.01) return;

        // The player has the camera now; stop steering it round to the car
        _alphaGoal = null;

        orbit.Alpha += (float)dx * 0.01f;
        orbit.Beta = Math.Clamp(orbit.Beta + (float)dy * 0.01f, MinBeta, MaxBeta);
        _betaGoal = orbit.Beta;

        ShowLabel(null, default);
        Invalidate();
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = -1;
        ShowLabel(null, default);
        Cursor = System.Windows.Input.Cursors.Arrow;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        var orbit = Renderer?.CameraOrbit;
        if (orbit == null) return;

        var min = IsCarView ? MinCarRadius : (float)LotRadius * LotZoomIn;
        var max = IsCarView ? MaxCarRadius : (float)LotRadius * LotZoomOut;

        // Move by a share of where the camera is, so the wheel feels the same close up and far out
        _radiusGoal = Math.Clamp(_radiusGoal * (1f - e.Delta * 0.0006f), min, max);
        Invalidate();
    }

    #endregion

    #region Body rock

    /// <summary>The engine of the car being looked at moved its body</summary>
    protected override void OnPoseChanged()
    {
        var index = SelectedIndex;
        var carNode = Renderer != null && index >= 0 && index < _slots.Count && index < _standing.Count && _standing[index] != null
            ? _slots[index]?.CarNode
            : null;
        RockCar(carNode);
    }

    #endregion

    #region Rendering

    protected override bool StepFrame(GarageRenderer renderer, float dt) => StepCamera(dt);

    protected override void OnFramePresented()
    {
        // An empty room filling up one car at a time is not worth watching. The picture is held back
        // until the lot is stocked, then faded up whole.
        if (IsLotReady && IsPictureHidden) FadeIn();
    }

    protected override void OnRendererDisposed()
    {
        _loadedShowroom = null;
        _slots.Clear();
        _standing.Clear();
        _loaded = [];
        LoadedCount = 0;
        IsLotReady = false;
        _hovered = -1;
    }

    #endregion
}
