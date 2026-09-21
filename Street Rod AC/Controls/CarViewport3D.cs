using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AcTools.Render.Kn5Specific.Objects;
using AcTools.Render.Kn5SpecificForwardDark;
using Street_Rod_AC.Configuration;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Parts;
using D3D9 = Vortice.Direct3D9;
using D3D11 = SlimDX.Direct3D11;

namespace Street_Rod_AC.Controls;

/// <summary>
/// Real-time 3D car viewport backed by the AcTools renderer.
/// The renderer draws off-screen into a shared DX11 texture which is presented through a D3DImage,
/// so the viewport composes like any other WPF element (overlays, opacity, dialogs on top all work).
/// </summary>
public class CarViewport3D : System.Windows.Controls.Grid
{
    // Orbit camera limits: stay above the floor and inside the showroom walls
    private const float MinBeta = 0.02f;
    private const float MaxBeta = 1.2f;
    private const float MinRadius = 3.2f;
    private const float MaxRadius = 9.0f;
    private const float DefaultRadius = 6.5f;
    private const float DefaultAlpha = 0.8f;
    private const float DefaultBeta = 0.15f;
    private const float EmptyGarageEyeHeight = 1.5f;

    // With the parts on show there is something worth a closer look
    private const float PartsMinRadius = 1.6f;

    // Keep drawing for a while after a toggle so door/light animations play out
    private static readonly TimeSpan AnimationWindow = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FadeInDuration = TimeSpan.FromMilliseconds(600);

    // D3DImage can drop the first frame after a back buffer swap, and the shared surface is read without
    // GPU sync, so a lone frame may never show up. Keep drawing briefly after every change instead.
    private static readonly TimeSpan SettleWindow = TimeSpan.FromMilliseconds(300);

    private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("Viewport3D");
    private readonly System.Windows.Controls.Image _image;
    private readonly D3DImage _d3dImage = new();

    private GarageRenderer? _renderer;
    private D3D9.IDirect3D9Ex? _d3d9;
    private D3D9.IDirect3DDevice9Ex? _d3d9Device;
    private D3D9.IDirect3DTexture9? _sharedTexture;
    private D3D9.IDirect3DSurface9? _sharedSurface;
    private IntPtr _boundTarget;

    private string? _loadedCarDirectory;
    private bool _isLoading;
    private bool _reloadRequested;
    private bool _failed;
    private TimeSpan _lastRenderingTime;
    private DateTime _animateUntil = DateTime.MinValue;
    private System.Windows.Point _lastMouse;
    private bool _isDragging;
    private bool _isLoadingParts;
    private string? _partsCarDirectory;

    public CarViewport3D()
    {
        Background = System.Windows.Media.Brushes.Transparent;
        ClipToBounds = true;
        Focusable = false;

        _image = new System.Windows.Controls.Image
        {
            Source = _d3dImage,
            Stretch = Stretch.Fill,
            Opacity = 0
        };
        Children.Add(_image);

        Loaded += (_, _) => RequestLoad();
        Unloaded += (_, _) => DisposeRenderer();
        IsVisibleChanged += (_, _) => RequestLoad();
        SizeChanged += (_, _) => UpdateRendererSize();
        _d3dImage.IsFrontBufferAvailableChanged += OnFrontBufferAvailableChanged;
    }

    /// <summary>Raised once when the first 3D frame is on screen</summary>
    public event EventHandler? Ready;

    /// <summary>Raised if the renderer could not be started or crashed</summary>
    public event EventHandler? Failed;

    /// <summary>
    /// Fade the viewport in by itself when the first frame is ready.
    /// Turn off when the host reveals the whole screen at once.
    /// </summary>
    public bool FadeInOnReady { get; set; } = true;

    #region Dependency properties

    public static readonly DependencyProperty CarDirectoryProperty = DependencyProperty.Register(
        nameof(CarDirectory), typeof(string), typeof(CarViewport3D),
        new PropertyMetadata(null, (d, _) => ((CarViewport3D)d).RequestLoad()));

    /// <summary>Full path to the AC car folder (content/cars/{id})</summary>
    public string? CarDirectory
    {
        get => (string?)GetValue(CarDirectoryProperty);
        set => SetValue(CarDirectoryProperty, value);
    }

    public static readonly DependencyProperty SkinIdProperty = DependencyProperty.Register(
        nameof(SkinId), typeof(string), typeof(CarViewport3D),
        new PropertyMetadata(null, (d, _) => ((CarViewport3D)d).ApplySkin()));

    public string? SkinId
    {
        get => (string?)GetValue(SkinIdProperty);
        set => SetValue(SkinIdProperty, value);
    }

    public static readonly DependencyProperty ShowroomKn5Property = DependencyProperty.Register(
        nameof(ShowroomKn5), typeof(string), typeof(CarViewport3D), new PropertyMetadata(null));

    /// <summary>Full path to the showroom .kn5 used as the garage environment (optional)</summary>
    public string? ShowroomKn5
    {
        get => (string?)GetValue(ShowroomKn5Property);
        set => SetValue(ShowroomKn5Property, value);
    }

    public static readonly DependencyProperty HeadlightsOnProperty = DependencyProperty.Register(
        nameof(HeadlightsOn), typeof(bool), typeof(CarViewport3D),
        new PropertyMetadata(false, (d, _) => ((CarViewport3D)d).ApplyCarState()));

    public bool HeadlightsOn
    {
        get => (bool)GetValue(HeadlightsOnProperty);
        set => SetValue(HeadlightsOnProperty, value);
    }

    public static readonly DependencyProperty DoorsOpenProperty = DependencyProperty.Register(
        nameof(DoorsOpen), typeof(bool), typeof(CarViewport3D),
        new PropertyMetadata(false, (d, _) => ((CarViewport3D)d).ApplyCarState()));

    public bool DoorsOpen
    {
        get => (bool)GetValue(DoorsOpenProperty);
        set => SetValue(DoorsOpenProperty, value);
    }

    public static readonly DependencyProperty AutoRotateProperty = DependencyProperty.Register(
        nameof(AutoRotate), typeof(bool), typeof(CarViewport3D),
        new PropertyMetadata(false, (d, _) => ((CarViewport3D)d).ApplyCarState()));

    public bool AutoRotate
    {
        get => (bool)GetValue(AutoRotateProperty);
        set => SetValue(AutoRotateProperty, value);
    }

    public static readonly DependencyProperty PartsVisibleProperty = DependencyProperty.Register(
        nameof(PartsVisible), typeof(bool), typeof(CarViewport3D),
        new PropertyMetadata(false, (d, _) => ((CarViewport3D)d).ApplyParts()));

    /// <summary>X-ray view: the car fades to a shell and its parts show in their places</summary>
    public bool PartsVisible
    {
        get => (bool)GetValue(PartsVisibleProperty);
        set => SetValue(PartsVisibleProperty, value);
    }

    private static readonly DependencyPropertyKey IsReadyPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsReady), typeof(bool), typeof(CarViewport3D), new PropertyMetadata(false));

    public static readonly DependencyProperty IsReadyProperty = IsReadyPropertyKey.DependencyProperty;

    /// <summary>True once the first 3D frame is on screen. Until then the viewport is transparent.</summary>
    public bool IsReady
    {
        get => (bool)GetValue(IsReadyProperty);
        private set => SetValue(IsReadyPropertyKey, value);
    }

    private static readonly DependencyPropertyKey HasFailedPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasFailed), typeof(bool), typeof(CarViewport3D), new PropertyMetadata(false));

    public static readonly DependencyProperty HasFailedProperty = HasFailedPropertyKey.DependencyProperty;

    /// <summary>True if the 3D renderer could not be started; hosts should show a 2D fallback</summary>
    public bool HasFailed
    {
        get => (bool)GetValue(HasFailedProperty);
        private set => SetValue(HasFailedPropertyKey, value);
    }

    private static readonly DependencyPropertyKey HasCarPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasCar), typeof(bool), typeof(CarViewport3D), new PropertyMetadata(false));

    public static readonly DependencyProperty HasCarProperty = HasCarPropertyKey.DependencyProperty;

    /// <summary>True if a car model is loaded (false when only the empty showroom is shown)</summary>
    public bool HasCar
    {
        get => (bool)GetValue(HasCarProperty);
        private set => SetValue(HasCarPropertyKey, value);
    }

    private static readonly DependencyPropertyKey HasDoorsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasDoors), typeof(bool), typeof(CarViewport3D), new PropertyMetadata(false));

    public static readonly DependencyProperty HasDoorsProperty = HasDoorsPropertyKey.DependencyProperty;

    /// <summary>True if the loaded car model has door animations</summary>
    public bool HasDoors
    {
        get => (bool)GetValue(HasDoorsProperty);
        private set => SetValue(HasDoorsPropertyKey, value);
    }

    #endregion

    #region Loading

    private async void RequestLoad()
    {
        if (!IsLoaded || !IsVisible || _failed) return;

        // An empty directory means "no car": only the showroom is rendered
        var carDirectory = CarDirectory ?? string.Empty;
        if (_renderer != null && carDirectory == _loadedCarDirectory) return;

        if (_isLoading)
        {
            _reloadRequested = true;
            return;
        }

        _isLoading = true;
        try
        {
            await LoadCarAsync(carDirectory);
        }
        catch (Exception ex)
        {
            // Leave the viewport transparent so whatever is behind it stays visible
            _logger.Error(ex, "3D viewport failed for {CarDirectory}", carDirectory);
            Fail();
        }
        finally
        {
            _isLoading = false;
        }

        if (_reloadRequested)
        {
            _reloadRequested = false;
            RequestLoad();
        }
    }

    private async Task LoadCarAsync(string carDirectory)
    {
        var hasCar = carDirectory.Length > 0;
        if (hasCar && !Directory.Exists(carDirectory))
            throw new DirectoryNotFoundException($"Car folder not found: {carDirectory}");

        var started = DateTime.Now;
        var car = hasCar ? CarDescription.FromDirectory(carDirectory) : null;
        var skinId = SkinId;

        if (_renderer == null)
        {
            var showroom = ShowroomKn5;
            if (showroom != null && !File.Exists(showroom))
            {
                _logger.Warning("Showroom not found, rendering without it: {Showroom}", showroom);
                showroom = null;
            }

            if (car == null && showroom == null)
                throw new InvalidOperationException("Nothing to render: no car and no showroom");

            var (width, height) = GetPixelSize();
            var renderer = new GarageRenderer(car, showroom)
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
                Width = width,
                Height = height
            };

            try
            {
                await Task.Run(() => renderer.Initialize());
                EnsureD3D9Device();
            }
            catch
            {
                renderer.Dispose();
                throw;
            }

            // The control may have been unloaded while the car was loading
            if (!IsLoaded)
            {
                renderer.Dispose();
                return;
            }

            _renderer = renderer;
            ResetCamera();
            CompositionTarget.Rendering += OnRendering;
        }
        else
        {
            await _renderer.MainSlot.SetCarAsync(car, skinId ?? Kn5RenderableCar.DefaultSkin);
            ResetCamera();
        }

        _loadedCarDirectory = carDirectory;
        HasCar = _renderer.CarNode != null;
        HasDoors = _renderer.CarNode?.HasLeftDoorAnimation == true || _renderer.CarNode?.HasRightDoorAnimation == true;
        ApplySkin();
        ApplyCarState();
        ApplyParts();
        _renderer.IsDirty = true;

        _logger.Information("Loaded {Car} in {Ms} ms", hasCar ? Path.GetFileName(carDirectory) : "(empty garage)",
            (int)(DateTime.Now - started).TotalMilliseconds);
    }

    private void ResetCamera()
    {
        var orbit = _renderer?.CameraOrbit;
        if (orbit == null) return;

        orbit.Radius = DefaultRadius;
        orbit.Alpha = DefaultAlpha;
        orbit.Beta = DefaultBeta;

        if (_renderer!.CarNode == null)
        {
            // Empty garage: nothing to frame, so look across the room at eye level instead of at the floor
            _renderer.AutoAdjustTarget = false;
            orbit.Target = new SlimDX.Vector3(0f, EmptyGarageEyeHeight, 0f);
            orbit.Radius = MaxRadius;
            orbit.Beta = MinBeta;
        }
        else
        {
            _renderer.AutoAdjustTarget = true;
        }
    }

    private async void ApplyParts()
    {
        var renderer = _renderer;
        if (renderer == null || _isLoadingParts) return;

        var carDirectory = _loadedCarDirectory;
        var carNode = renderer.CarNode;
        if (!PartsVisible || carNode == null || string.IsNullOrEmpty(carDirectory))
        {
            // Without a car there is nothing to dissolve back into
            if (carNode == null)
            {
                renderer.GhostCar = false;
                renderer.SetProp(null, SlimDX.Matrix.Identity);
            }
            else
            {
                renderer.HideParts();
            }

            _partsCarDirectory = null;
            _animateUntil = DateTime.Now + AnimationWindow;
            return;
        }

        // The layout depends on the car, so a car swap rebuilds it
        if (renderer.HasProp && _partsCarDirectory == carDirectory) return;

        var anchors = GetAnchors(carNode);
        if (anchors == null)
        {
            _logger.Warning("Car has no wheel nodes to place the parts by: {Car}", carDirectory);
            return;
        }

        _isLoadingParts = true;
        try
        {
            var settings = AppSettings.Instance;
            var engine = GuessEngine(Path.GetFileName(carDirectory)) ?? settings.GarageEnginePart;
            var started = DateTime.Now;

            var model = await Task.Run(() =>
            {
                var catalog = PartsCatalog.Load(settings.PartsPath);
                var parts = CarPartsLayout.Build(catalog, anchors, engine);
                return PartAssembler.BuildModel(catalog, "parts", parts);
            });

            // The renderer or the car may have been replaced, or the toggle flipped back, while loading
            if (renderer == _renderer && PartsVisible && _loadedCarDirectory == carDirectory)
            {
                renderer.SetProp(model.Kn5, SlimDX.Matrix.Identity);
                renderer.GhostCar = true;
                _partsCarDirectory = carDirectory;
                _animateUntil = DateTime.Now + AnimationWindow;

                _logger.Information("Parts of {Car} laid out in {Ms} ms (engine {Engine})", Path.GetFileName(carDirectory),
                    (int)(DateTime.Now - started).TotalMilliseconds, engine);
            }
        }
        catch (Exception ex)
        {
            // The garage works fine without the parts view, so just leave it off
            _logger.Error(ex, "Could not lay out the parts");
        }
        finally
        {
            _isLoadingParts = false;
        }

        // Catch up with whatever changed in the meantime
        if (_renderer != null && (!PartsVisible || _loadedCarDirectory != carDirectory)) ApplyParts();
    }

    private static CarAnchors? GetAnchors(Kn5RenderableCar carNode)
    {
        var hubs = new[] { "WHEEL_LF", "WHEEL_RF", "WHEEL_LR", "WHEEL_RR" }
            .Select(name => carNode.RootObject.GetDummyByName(name)?.Matrix)
            .ToArray();
        if (hubs.Any(h => h == null)) return null;

        var points = hubs.Select(h => new System.Numerics.Vector3(h!.Value.M41, h.Value.M42, h.Value.M43)).ToArray();
        return new CarAnchors(points[0], points[1], points[2], points[3]);
    }

    /// <summary>
    /// Stand-in until cars carry their own parts: pick an engine of the right family from the car's name
    /// </summary>
    private static string? GuessEngine(string carId)
    {
        var id = carId.ToLowerInvariant();
        if (new[] { "chevy", "chevrolet", "camaro", "vette", "corvette", "pontiac", "gto" }.Any(id.Contains))
            return "engines/GM_V8_pak/GM_327_block";
        if (new[] { "ford", "shelby", "mustang", "falcon", "cobra" }.Any(id.Contains))
            return "engines/DEXTERV8s/Ford_302_block";
        if (new[] { "chrysler", "dodge", "plymouth", "valiant", "mopar" }.Any(id.Contains))
            return "engines/Mopar/block_340";
        return null;
    }

    private void ApplySkin()
    {
        if (_renderer == null || string.IsNullOrEmpty(_loadedCarDirectory)) return;

        var skinId = SkinId;
        if (string.IsNullOrEmpty(skinId)) return;

        if (Directory.Exists(Path.Combine(_loadedCarDirectory, "skins", skinId)))
        {
            _renderer.SelectSkin(skinId);
            _renderer.IsDirty = true;
        }
    }

    private void ApplyCarState()
    {
        var renderer = _renderer;
        if (renderer == null) return;

        renderer.AutoRotate = AutoRotate;

        var carNode = renderer.CarNode;
        if (carNode != null)
        {
            carNode.HeadlightsEnabled = HeadlightsOn;
            if (carNode.HasLeftDoorAnimation) carNode.LeftDoorOpen = DoorsOpen;
            if (carNode.HasRightDoorAnimation) carNode.RightDoorOpen = DoorsOpen;
        }

        _animateUntil = DateTime.Now + AnimationWindow;
        renderer.IsDirty = true;
    }

    #endregion

    #region Rendering

    private void OnRendering(object? sender, EventArgs e)
    {
        // CompositionTarget.Rendering can fire more than once per frame
        var args = (RenderingEventArgs)e;
        if (args.RenderingTime == _lastRenderingTime) return;
        _lastRenderingTime = args.RenderingTime;

        var renderer = _renderer;
        if (renderer == null || !IsVisible || !_d3dImage.IsFrontBufferAvailable) return;

        var now = DateTime.Now;
        if (renderer.IsDirty && _animateUntil < now + SettleWindow)
        {
            _animateUntil = now + SettleWindow;
        }

        var animating = renderer.AutoRotate || now < _animateUntil;
        if (!animating && _boundTarget != IntPtr.Zero) return;

        try
        {
            renderer.Draw();

            // The render target is recreated on resize, so rebind whenever the pointer changes
            var target = renderer.GetRenderTarget();
            if (target != _boundTarget)
            {
                BindRenderTarget(target);
            }

            _d3dImage.Lock();
            _d3dImage.AddDirtyRect(new Int32Rect(0, 0, _d3dImage.PixelWidth, _d3dImage.PixelHeight));
            _d3dImage.Unlock();

            if (!IsReady)
            {
                IsReady = true;
                if (FadeInOnReady)
                {
                    _image.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, FadeInDuration));
                }
                else
                {
                    _image.Opacity = 1;
                }
                Ready?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "3D viewport render loop failed");
            Fail();
        }
    }

    private void EnsureD3D9Device()
    {
        if (_d3d9Device != null) return;

        _d3d9 = D3D9.D3D9.Direct3DCreate9Ex();
        var parameters = new D3D9.PresentParameters
        {
            Windowed = true,
            SwapEffect = D3D9.SwapEffect.Discard,
            DeviceWindowHandle = GetDesktopWindow(),
            PresentationInterval = D3D9.PresentInterval.Immediate,
            BackBufferWidth = 1,
            BackBufferHeight = 1
        };

        _d3d9Device = _d3d9.CreateDeviceEx(0, D3D9.DeviceType.Hardware, IntPtr.Zero,
            D3D9.CreateFlags.HardwareVertexProcessing | D3D9.CreateFlags.Multithreaded | D3D9.CreateFlags.FpuPreserve,
            parameters);
    }

    /// <summary>
    /// Opens the renderer's shared DX11 texture on the D3D9Ex device and hands its surface to the D3DImage
    /// </summary>
    private void BindRenderTarget(IntPtr target)
    {
        ReleaseSharedSurface();

        // Owned by the renderer - do not dispose
        var texture = D3D11.Texture2D.FromPointer(target);
        var description = texture.Description;

        IntPtr sharedHandle;
        using (var resource = new SlimDX.DXGI.Resource(texture))
        {
            sharedHandle = resource.SharedHandle;
        }

        if (sharedHandle == IntPtr.Zero)
            throw new InvalidOperationException("Render target is not a shared resource");

        _sharedTexture = _d3d9Device!.CreateTexture((uint)description.Width, (uint)description.Height, 1,
            D3D9.Usage.RenderTarget, D3D9.Format.A8R8G8B8, D3D9.Pool.Default, ref sharedHandle);
        _sharedSurface = _sharedTexture.GetSurfaceLevel(0);

        _d3dImage.Lock();
        _d3dImage.SetBackBuffer(D3DResourceType.IDirect3DSurface9, _sharedSurface.NativePointer);
        _d3dImage.Unlock();

        _boundTarget = target;
    }

    private void ReleaseSharedSurface()
    {
        if (_boundTarget != IntPtr.Zero)
        {
            _d3dImage.Lock();
            _d3dImage.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero);
            _d3dImage.Unlock();
        }

        _sharedSurface?.Dispose();
        _sharedSurface = null;
        _sharedTexture?.Dispose();
        _sharedTexture = null;
        _boundTarget = IntPtr.Zero;
    }

    private void OnFrontBufferAvailableChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Front buffer is lost on lock screen / remote desktop; force a rebind and redraw when it returns
        if (_d3dImage.IsFrontBufferAvailable && _renderer != null)
        {
            _boundTarget = IntPtr.Zero;
            _renderer.IsDirty = true;
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

        ReleaseSharedSurface();

        _renderer?.Dispose();
        _renderer = null;
        _loadedCarDirectory = null;

        _d3d9Device?.Dispose();
        _d3d9Device = null;
        _d3d9?.Dispose();
        _d3d9 = null;

        IsReady = false;
        HasCar = false;
        HasDoors = false;
        _image.BeginAnimation(OpacityProperty, null);
        _image.Opacity = 0;
    }

    private void Fail()
    {
        _failed = true;
        DisposeRenderer();
        HasFailed = true;
        Failed?.Invoke(this, EventArgs.Empty);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    #endregion

    #region Camera input

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_renderer == null) return;

        _isDragging = true;
        _lastMouse = e.GetPosition(this);
        CaptureMouse();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _isDragging = false;
        ReleaseMouseCapture();
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var orbit = _renderer?.CameraOrbit;
        if (!_isDragging || orbit == null) return;

        var position = e.GetPosition(this);
        orbit.Alpha += (float)(position.X - _lastMouse.X) * 0.01f;
        orbit.Beta = Math.Clamp(orbit.Beta + (float)(position.Y - _lastMouse.Y) * 0.01f, MinBeta, MaxBeta);
        _lastMouse = position;

        _renderer!.IsDirty = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        var orbit = _renderer?.CameraOrbit;
        if (orbit == null) return;

        var minRadius = _renderer!.HasProp ? PartsMinRadius : MinRadius;
        orbit.Radius = Math.Clamp(orbit.Radius - e.Delta * 0.002f, minRadius, MaxRadius);
        _renderer!.IsDirty = true;
        e.Handled = true;
    }

    #endregion
}
