using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AcTools.Render.Kn5Specific.Objects;
using AcTools.Render.Kn5SpecificForward;
using Street_Rod_AC.Logging;
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
public class DealerLotViewport3D : System.Windows.Controls.Grid
{
    // Taking in the whole lot, and standing on one car
    private const float LotRadius = 19f;
    private const float LotBeta = 0.30f;
    private const float LotEyeHeight = 1.0f;
    private const float CarRadius = 6.2f;
    private const float CarBeta = 0.14f;

    // What the wheel is allowed to do, in each of the two views
    private const float MinLotRadius = 12f;
    private const float MaxLotRadius = 30f;
    private const float MinCarRadius = 3.4f;
    private const float MaxCarRadius = 11f;

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
    private static readonly TimeSpan FadeInDuration = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan SettleWindow = TimeSpan.FromMilliseconds(300);

    private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("DealerLot");
    private readonly System.Windows.Controls.Image _image;
    private readonly SharedTextureBridge _bridge = new();
    private readonly System.Windows.Controls.Border _label;
    private readonly System.Windows.Controls.TextBlock _labelText;

    private GarageRenderer? _renderer;

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

    private bool _isLoading;
    private bool _reloadRequested;
    private bool _failed;
    private TimeSpan _lastRenderingTime;
    private DateTime _animateUntil = DateTime.MinValue;

    private System.Windows.Point _lastMouse;
    private System.Windows.Point _pressedAt;
    private bool _isDragging;
    private DateTime _lastHoverPick = DateTime.MinValue;
    private int _hovered = -1;

    // Where the camera is going. Alpha is only steered until the player takes hold of it
    private Sx.Vector3 _targetGoal = new(0f, LotEyeHeight, 0f);
    private float _radiusGoal = LotRadius;
    private float _betaGoal = LotBeta;
    private float? _alphaGoal;

    public DealerLotViewport3D()
    {
        Background = System.Windows.Media.Brushes.Transparent;
        ClipToBounds = true;
        Focusable = false;

        _image = new System.Windows.Controls.Image
        {
            Source = _bridge.Image,
            Stretch = Stretch.Fill,
            Opacity = 0
        };
        Children.Add(_image);

        _labelText = new System.Windows.Controls.TextBlock
        {
            Foreground = System.Windows.Media.Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold
        };
        _label = new System.Windows.Controls.Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xD8, 0x10, 0x10, 0x10)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x90, 0xFF, 0xE6, 0x8C)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(9, 5, 9, 5),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Child = _labelText
        };
        Children.Add(_label);

        Loaded += (_, _) => RequestLoad();
        Unloaded += (_, _) =>
        {
            DisposeRenderer();
            _bridge.Dispose();
        };
        IsVisibleChanged += (_, _) => RequestLoad();
        SizeChanged += (_, _) => UpdateRendererSize();
        _bridge.FrontBufferRestored += (_, _) =>
        {
            if (_renderer != null) _renderer.IsDirty = true;
        };
    }

    /// <summary>Raised once when the first 3D frame is on screen</summary>
    public event EventHandler? Ready;

    /// <summary>Raised if the renderer could not be started or crashed</summary>
    public event EventHandler? Failed;

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

    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex), typeof(int), typeof(DealerLotViewport3D),
        new PropertyMetadata(-1, (d, _) => ((DealerLotViewport3D)d).OnSelectionChanged()));

    /// <summary>Which car the camera sits on, or -1 to take in the whole lot</summary>
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    private static readonly DependencyPropertyKey IsReadyPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsReady), typeof(bool), typeof(DealerLotViewport3D), new PropertyMetadata(false));

    public static readonly DependencyProperty IsReadyProperty = IsReadyPropertyKey.DependencyProperty;

    /// <summary>The first frame is on screen</summary>
    public bool IsReady
    {
        get => (bool)GetValue(IsReadyProperty);
        private set => SetValue(IsReadyPropertyKey, value);
    }

    private static readonly DependencyPropertyKey HasFailedPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasFailed), typeof(bool), typeof(DealerLotViewport3D), new PropertyMetadata(false));

    public static readonly DependencyProperty HasFailedProperty = HasFailedPropertyKey.DependencyProperty;

    /// <summary>The 3D lot could not be shown; the host should fall back to a flat list</summary>
    public bool HasFailed
    {
        get => (bool)GetValue(HasFailedProperty);
        private set => SetValue(HasFailedPropertyKey, value);
    }

    public static readonly DependencyProperty LoadedCountProperty = DependencyProperty.Register(
        nameof(LoadedCount), typeof(int), typeof(DealerLotViewport3D), new PropertyMetadata(0));

    /// <summary>
    /// How many cars have made it onto the lot so far. Written by the viewport and meant to be read with a
    /// OneWayToSource binding: only the viewport knows how many cars the budget let it stand up.
    /// </summary>
    public int LoadedCount
    {
        get => (int)GetValue(LoadedCountProperty);
        set => SetValue(LoadedCountProperty, value);
    }

    #endregion

    #region Loading

    private async void RequestLoad()
    {
        if (!IsLoaded || !IsVisible || _failed) return;

        var cars = Cars ?? [];
        if (_renderer != null && SameCars(cars, _loaded)) return;

        if (_isLoading)
        {
            _reloadRequested = true;
            return;
        }

        _isLoading = true;
        try
        {
            await LoadLotAsync(cars);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Dealer lot viewport failed");
            Fail();
        }
        finally
        {
            _isLoading = false;
            if (_reloadRequested)
            {
                _reloadRequested = false;
                RequestLoad();
            }
        }
    }

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
            _logger.Warning("Showroom not found, rendering the lot without it: {Showroom}", showroom);
            showroom = null;
        }

        if (showroom == null && cars.Count == 0)
            throw new InvalidOperationException("Nothing to render: no showroom and no cars");

        // Selling a car changes the stock but not the place it is sold in. Rebuilding the scene would mean
        // reading a showroom of a few hundred MB again for nothing, so only the cars are swapped.
        if (_renderer != null && showroom == _loadedShowroom)
        {
            await UpdateCarsAsync(cars);
            return;
        }

        DisposeRenderer();
        _failed = false;

        var started = DateTime.Now;
        var (width, height) = GetPixelSize();

        // The cars go in one at a time afterwards, so the scene starts as the empty showroom
        var renderer = new GarageRenderer(null, showroom)
        {
            WpfMode = true,
            UseMsaa = false, // shared surfaces can't be multisampled
            VisibleUi = false,
            AutoRotate = false,
            UseFxaa = true,
            UseSslr = true,
            UseAo = true,
            UseBloom = true,
            EnableShadows = true,
            UsePcss = true,

            // Every car would otherwise bring its own headlights and tail lights along. A lot full of them runs
            // into the shader's own ceiling on how many lights a scene may have, for lamps nobody is looking at.
            // The ambient cubemap is left alone: the cars are meant to sit in the same light as in the garage,
            // and its cost is one small probe per car, one of them refreshed per frame. If a full lot turns out
            // to be slow, CubemapAmbient = 0 switches those probes off and is the first thing to try.
            LoadCarLights = false,
            TryToGuessCarLights = false,

            // The camera is driven from here; the renderer must not glide it onto a car of its own accord
            AutoAdjustTarget = false,

            Width = width,
            Height = height
        };

        try
        {
            await Task.Run(() => renderer.Initialize());
        }
        catch
        {
            renderer.Dispose();
            throw;
        }

        // The showroom takes seconds to read, and the screen may be gone by now. Check before raising a
        // device, or one is left behind on a control that will never draw again.
        if (!IsLoaded)
        {
            renderer.Dispose();
            return;
        }

        _bridge.EnsureDevice();

        _renderer = renderer;
        _loadedShowroom = showroom;
        _loaded = cars;
        _slots.Clear();
        LoadedCount = 0;

        ApplyLotCamera(immediate: true);
        if (SelectedIndex >= 0) OnSelectionChanged();
        CompositionTarget.Rendering += OnRendering;

        _logger.Information("Lot showroom up in {Ms} ms, {Count} cars to place",
            (int)(DateTime.Now - started).TotalMilliseconds, cars.Count);

        await LoadCarsAsync(cars);
    }

    /// <summary>
    /// Swaps the cars on a lot that is already standing. A slot whose car has not changed is left alone, so
    /// selling one car off a lot of six costs the loading of the ones that shuffled up, not of the whole place.
    /// </summary>
    private async Task UpdateCarsAsync(IReadOnlyList<LotCarPlacement> cars)
    {
        var renderer = _renderer;
        if (renderer == null) return;

        var previous = _loaded;
        _loaded = cars;

        var budget = CarByteBudget;

        for (var i = 0; i < cars.Count; i++)
        {
            if (_renderer != renderer || !IsLoaded) return;

            var car = cars[i];
            if (i < previous.Count && i < _standing.Count && _standing[i] is { } already && already.Equals(car))
            {
                // This bay already holds exactly this car
                budget -= EstimateCarBytes(car.CarDirectory);
                continue;
            }

            while (_slots.Count <= i) _slots.Add(null);
            while (_standing.Count <= i) _standing.Add(null);

            // Whatever was here is not what belongs here. Take it off before anything else: leaving it would
            // mean the old car standing under the new car's name, and a click on it buying the wrong car.
            await ClearBayAsync(i, renderer);
            if (_renderer != renderer || !IsLoaded) return;

            if (!Directory.Exists(car.CarDirectory))
            {
                _logger.Warning("Car folder not found, left off the lot: {Directory}", car.CarDirectory);
                continue;
            }

            var bytes = EstimateCarBytes(car.CarDirectory);
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
                await slot.SetCarAsync(CarDescription.FromDirectory(car.CarDirectory),
                    car.SkinId ?? Kn5RenderableCar.DefaultSkin);

                if (_renderer != renderer || !IsLoaded) return;

                Place(slot, car);
                _slots[i] = slot;
                _standing[i] = car;
                renderer.IsDirty = true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not put {Car} on the lot", Path.GetFileName(car.CarDirectory));
            }
        }

        // Anything the lot no longer sells is taken off it
        for (var i = cars.Count; i < _slots.Count; i++)
        {
            await ClearBayAsync(i, renderer);
            if (_renderer != renderer || !IsLoaded) return;
        }

        LoadedCount = _standing.Count(c => c != null);
        renderer.IsDirty = true;
        _animateUntil = DateTime.Now + SettleWindow;

        _logger.Information("Lot restocked: {Loaded} of {Total} cars out front", LoadedCount, cars.Count);
    }

    /// <summary>
    /// Empties a bay: the car goes, and the slot is handed back for whatever needs one next. Leaving the slot
    /// in place but forgetting it would strand the main slot, which the whole scene's shadows hang off.
    /// </summary>
    private async Task ClearBayAsync(int index, GarageRenderer renderer)
    {
        if (index >= _slots.Count || _slots[index] is not { } slot) return;

        try
        {
            await slot.SetCarAsync(null);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not clear a car off the lot");
        }

        if (_renderer != renderer) return;

        _slots[index] = null;
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
            var renderer = _renderer;
            if (renderer == null || !IsLoaded) return;

            var car = cars[i];
            if (!Directory.Exists(car.CarDirectory))
            {
                _logger.Warning("Car folder not found, left off the lot: {Directory}", car.CarDirectory);
                continue;
            }

            var bytes = EstimateCarBytes(car.CarDirectory);
            if (bytes > budget)
            {
                _logger.Information("Lot is full at {Count} cars; {Car} ({Mb} MB) stays in the list only",
                    LoadedCount, Path.GetFileName(car.CarDirectory), bytes / (1024 * 1024));
                continue;
            }
            budget -= bytes;

            try
            {
                var slot = i == main ? renderer.MainSlot : renderer.AddCar(null);

                // Before and after: see the note in UpdateCarsAsync
                Place(slot, car);
                await slot.SetCarAsync(CarDescription.FromDirectory(car.CarDirectory),
                    car.SkinId ?? Kn5RenderableCar.DefaultSkin);

                if (_renderer != renderer || !IsLoaded) return;

                Place(slot, car);
                _slots[i] = slot;
                _standing[i] = car;
                LoadedCount++;

                renderer.IsDirty = true;
                _animateUntil = DateTime.Now + SettleWindow;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not put {Car} on the lot", Path.GetFileName(car.CarDirectory));
            }
        }

        _logger.Information("Lot loaded: {Loaded} of {Total} cars", LoadedCount, cars.Count);
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

    /// <summary>
    /// Roughly what a car will cost to hold, taken as the biggest model in its folder. Good enough to tell a
    /// 15 MB coupe from a 400 MB one, which is all the budget needs to know.
    /// </summary>
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

    #region Camera

    private void OnSelectionChanged()
    {
        if (_renderer == null) return;

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

        _animateUntil = DateTime.Now + SettleWindow;
        _renderer.IsDirty = true;
    }

    private void ApplyLotCamera(bool immediate)
    {
        _targetGoal = new Sx.Vector3(0f, LotEyeHeight, 0f);
        _radiusGoal = LotRadius;
        _betaGoal = LotBeta;
        _alphaGoal = null;

        var orbit = _renderer?.CameraOrbit;
        if (orbit == null) return;

        if (immediate)
        {
            orbit.Target = _targetGoal;
            orbit.Radius = _radiusGoal;
            orbit.Beta = _betaGoal;
            orbit.Alpha = 0.9f;
        }

        _animateUntil = DateTime.Now + SettleWindow;
        if (_renderer != null) _renderer.IsDirty = true;
    }

    /// <summary>
    /// Eases the camera toward wherever it is meant to be. Returns true while it is still moving, so the
    /// render loop knows to keep drawing.
    /// </summary>
    private bool StepCamera(float dt)
    {
        var orbit = _renderer?.CameraOrbit;
        if (orbit == null) return false;

        var k = Math.Min(1f, dt * 6f);
        var moving = false;

        var target = orbit.Target;
        var toTarget = _targetGoal - target;
        if (toTarget.LengthSquared() > 1e-5f)
        {
            orbit.Target = target + toTarget * k;
            moving = true;
        }

        if (Math.Abs(_radiusGoal - orbit.Radius) > 1e-3f)
        {
            orbit.Radius += (_radiusGoal - orbit.Radius) * k;
            moving = true;
        }

        if (Math.Abs(_betaGoal - orbit.Beta) > 1e-4f)
        {
            orbit.Beta += (_betaGoal - orbit.Beta) * k;
            moving = true;
        }

        if (_alphaGoal is { } alphaGoal)
        {
            // Round the short way
            var delta = (float)Math.IEEERemainder(alphaGoal - orbit.Alpha, Math.PI * 2);
            if (Math.Abs(delta) > 1e-3f)
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
        var renderer = _renderer;
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

        if (hit < 0)
        {
            _label.Visibility = Visibility.Collapsed;
            return;
        }

        _labelText.Text = _loaded[hit].Label;
        _label.Margin = new Thickness(position.X + 18, position.Y + 18, 0, 0);
        _label.Visibility = Visibility.Visible;
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

        var orbit = _renderer?.CameraOrbit;
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

        _label.Visibility = Visibility.Collapsed;
        _animateUntil = DateTime.Now + SettleWindow;
        _renderer!.IsDirty = true;
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = -1;
        _label.Visibility = Visibility.Collapsed;
        Cursor = System.Windows.Input.Cursors.Arrow;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        var orbit = _renderer?.CameraOrbit;
        if (orbit == null) return;

        var min = IsCarView ? MinCarRadius : MinLotRadius;
        var max = IsCarView ? MaxCarRadius : MaxLotRadius;

        _radiusGoal = Math.Clamp(_radiusGoal - e.Delta * 0.004f, min, max);
        _animateUntil = DateTime.Now + SettleWindow;
        _renderer!.IsDirty = true;
    }

    #endregion

    #region Rendering

    private void OnRendering(object? sender, EventArgs e)
    {
        // CompositionTarget.Rendering can fire more than once per frame
        var args = (RenderingEventArgs)e;
        if (args.RenderingTime == _lastRenderingTime) return;

        var dt = (float)(args.RenderingTime - _lastRenderingTime).TotalSeconds;
        _lastRenderingTime = args.RenderingTime;
        if (dt <= 0f || dt > 0.25f) dt = 1f / 60f;

        var renderer = _renderer;
        if (renderer == null || !IsVisible || !_bridge.IsFrontBufferAvailable) return;

        var moving = StepCamera(dt);

        var now = DateTime.Now;
        if (renderer.IsDirty && _animateUntil < now + SettleWindow)
        {
            _animateUntil = now + SettleWindow;
        }

        if (!moving && now >= _animateUntil && _bridge.BoundTarget != IntPtr.Zero) return;

        try
        {
            renderer.Draw();
            _bridge.Present(renderer.GetRenderTarget());

            if (!IsReady)
            {
                IsReady = true;
                _image.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, FadeInDuration));
                Ready?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Dealer lot render loop failed");
            Fail();
        }
    }

    private (int Width, int Height) GetPixelSize()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = Math.Max(16, (int)Math.Round(ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(16, (int)Math.Round(ActualHeight * dpi.DpiScaleY));
        return (width, height);
    }

    private void UpdateRendererSize()
    {
        if (_renderer == null) return;

        var (width, height) = GetPixelSize();
        _renderer.Width = width;
        _renderer.Height = height;
        _renderer.IsDirty = true;
    }

    private void DisposeRenderer()
    {
        CompositionTarget.Rendering -= OnRendering;

        // The target belongs to the renderer: let go of it before the renderer goes
        _bridge.ReleaseSurface();

        _renderer?.Dispose();
        _renderer = null;
        _loadedShowroom = null;

        _slots.Clear();
        _standing.Clear();
        _loaded = [];
        LoadedCount = 0;
        IsReady = false;
        _hovered = -1;
        _label.Visibility = Visibility.Collapsed;

        _image.BeginAnimation(OpacityProperty, null);
        _image.Opacity = 0;
    }

    private void Fail()
    {
        _failed = true;
        DisposeRenderer();
        _bridge.Dispose();
        HasFailed = true;
        Failed?.Invoke(this, EventArgs.Empty);
    }

    #endregion
}
