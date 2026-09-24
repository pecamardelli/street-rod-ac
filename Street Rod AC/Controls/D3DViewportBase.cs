using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AcTools.Render.Kn5Specific.Objects;
using Street_Rod_AC.Logging;

namespace Street_Rod_AC.Controls;

/// <summary>
/// What every AcTools viewport on a WPF screen has in common: the renderer draws off-screen into a shared DX11
/// texture, a <see cref="SharedTextureBridge"/> hands it to a D3DImage, and the control composes like any other WPF
/// element. This owns the life of the renderer and the bridge (load, resize, DPI, the render pump, disposal, failure
/// and recovery from a lost device), the read-only state a screen binds to, the label next to the pointer and the
/// plumbing that rocks a car with its running engine.
///
/// What each viewport does with its scene stays with it. So do a few deliberate differences: the garage keeps drawing
/// while its car turns by itself and fades in on its first frame; the lot holds its picture back until every car is
/// standing; the lot keeps its D3D9 device across a change of showroom; only the garage leans its parts with the body.
/// </summary>
public abstract class D3DViewportBase : System.Windows.Controls.Grid
{
    /// <summary>
    /// D3DImage can drop the first frame after a back buffer swap, and the shared surface is read without GPU sync,
    /// so a lone frame may never show up. Keep drawing briefly after every change instead.
    /// </summary>
    protected static readonly TimeSpan SettleWindow = TimeSpan.FromMilliseconds(300);

    protected static readonly TimeSpan FadeInDuration = TimeSpan.FromMilliseconds(600);

    // A drag-resize is dozens of sizes a second. Each one would make the renderer new targets and the bridge a new
    // shared texture, so only the size the drag ends on is applied (the picture stretches meanwhile).
    private static readonly TimeSpan ResizeDelay = TimeSpan.FromMilliseconds(100);

    // While nothing moves the frame clock is let go. The renderer can still mark itself dirty from the inside (a
    // texture that finished loading), which is looked for this often.
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromMilliseconds(100);

    // A scene rebuilt after a lost device that still draws this long after is sound again: a later loss (the next
    // driver update, hours on) is rebuilt once more rather than failed
    private static readonly TimeSpan RecoveryGrace = TimeSpan.FromMinutes(1);

    // A GPU that went away under the renderer: a driver update or reset (TDR), or a hung device. DXGI's own codes
    // (DEVICE_REMOVED, DEVICE_HUNG, DEVICE_RESET, DRIVER_INTERNAL_ERROR) and D3D9's (DEVICELOST, DEVICEREMOVED, DEVICEHUNG)
    private static readonly HashSet<int> DeviceLostCodes =
    [
        unchecked((int)0x887A0005), unchecked((int)0x887A0006), unchecked((int)0x887A0007), unchecked((int)0x887A0020),
        unchecked((int)0x88760868), unchecked((int)0x88760870), unchecked((int)0x88760874)
    ];

    private readonly System.Windows.Controls.Image _image;
    private readonly SharedTextureBridge _bridge = new();
    private readonly System.Windows.Controls.Border _label;
    private readonly System.Windows.Controls.TextBlock _labelText;
    private readonly Vector _labelOffset;
    private readonly DispatcherTimer _resizeTimer;
    private readonly DispatcherTimer _idleTimer;

    private GarageRenderer? _renderer;

    // Device calls a car load is still making on the current renderer: it is disposed only once they are done
    private Task _deviceWork = Task.CompletedTask;

    private bool _isLoading;
    private bool _reloadRequested;
    private bool _failed;
    private bool _recovered;
    private DateTime _recoveredAt;
    private bool _pumping;
    private TimeSpan _lastRenderingTime;
    private DateTime _animateUntil = DateTime.MinValue;

    private Audio.EngineRunner? _poseSource;
    private CarBodyRock? _rock;

    /// <summary>How the label next to the pointer looks, and where it sits from the pointer</summary>
    protected readonly record struct LabelLook(double FontSize, byte BackgroundAlpha, byte BorderAlpha, Thickness Padding, Vector Offset);

    protected D3DViewportBase(string logCategory, LabelLook look)
    {
        Logger = AppLoggerFactory.CreateLogger(logCategory);

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
            FontSize = look.FontSize,
            FontWeight = FontWeights.SemiBold
        };
        _label = new System.Windows.Controls.Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(look.BackgroundAlpha, 0x10, 0x10, 0x10)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(look.BorderAlpha, 0xFF, 0xE6, 0x8C)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = look.Padding,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Child = _labelText
        };
        _labelOffset = look.Offset;
        Children.Add(_label);

        _resizeTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = ResizeDelay };
        _resizeTimer.Tick += (_, _) =>
        {
            _resizeTimer.Stop();
            UpdateRendererSize();
        };

        _idleTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = IdlePollInterval };
        _idleTimer.Tick += OnIdlePoll;

        Loaded += (_, _) =>
        {
            SetPoseSource(RunningEngine);
            RequestLoad();
        };
        Unloaded += (_, _) =>
        {
            SetPoseSource(null);
            DisposeRenderer(releaseDevice: true);

            // A failure is the failure of that visit: the next time the control is shown it tries again
            _failed = false;
            _recovered = false;
            HasFailed = false;
        };
        IsVisibleChanged += (_, _) =>
        {
            RequestLoad();
            if (IsVisible) Invalidate();
            else StopPump(idle: false);
        };
        SizeChanged += (_, _) =>
        {
            _resizeTimer.Stop();
            _resizeTimer.Start();
        };
        _bridge.FrontBufferRestored += (_, _) => Invalidate();
    }

    protected IAppLogger Logger { get; }

    /// <summary>The renderer on screen; null while there is none</summary>
    protected GarageRenderer? Renderer => _renderer;

    /// <summary>What this viewport is called in the log</summary>
    protected abstract string ViewportName { get; }

    /// <summary>Raised once when the first 3D frame is on screen</summary>
    public event EventHandler? Ready;

    /// <summary>Raised if the renderer could not be started or crashed</summary>
    public event EventHandler? Failed;

    #region Dependency properties

    public static readonly DependencyProperty RunningEngineProperty = DependencyProperty.Register(
        nameof(RunningEngine), typeof(Audio.EngineRunner), typeof(D3DViewportBase),
        new PropertyMetadata(null, (d, e) => ((D3DViewportBase)d).OnRunningEngineChanged((Audio.EngineRunner?)e.NewValue)));

    /// <summary>The engine of the car on show, started where it stands: the body rocks with it</summary>
    public Audio.EngineRunner? RunningEngine
    {
        get => (Audio.EngineRunner?)GetValue(RunningEngineProperty);
        set => SetValue(RunningEngineProperty, value);
    }

    private static readonly DependencyPropertyKey IsReadyPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsReady), typeof(bool), typeof(D3DViewportBase), new PropertyMetadata(false));

    public static readonly DependencyProperty IsReadyProperty = IsReadyPropertyKey.DependencyProperty;

    /// <summary>True once the first 3D frame is on screen. Until then the viewport is transparent.</summary>
    public bool IsReady
    {
        get => (bool)GetValue(IsReadyProperty);
        private set => SetValue(IsReadyPropertyKey, value);
    }

    private static readonly DependencyPropertyKey HasFailedPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasFailed), typeof(bool), typeof(D3DViewportBase), new PropertyMetadata(false));

    public static readonly DependencyProperty HasFailedProperty = HasFailedPropertyKey.DependencyProperty;

    /// <summary>True if the 3D renderer could not be started; hosts should show a 2D fallback</summary>
    public bool HasFailed
    {
        get => (bool)GetValue(HasFailedProperty);
        private set => SetValue(HasFailedPropertyKey, value);
    }

    #endregion

    #region Loading

    /// <summary>The scene on screen is the one the properties ask for: nothing to load</summary>
    protected abstract bool IsUpToDate { get; }

    /// <summary>Builds or updates the scene. Runs one at a time; a request made meanwhile runs after it.</summary>
    protected abstract Task LoadAsync();

    /// <summary>What was being loaded, for the log line of a load that failed</summary>
    protected virtual string LoadDescription => "";

    protected async void RequestLoad()
    {
        if (!IsLoaded || !IsVisible || _failed) return;
        if (IsUpToDate) return;

        if (_isLoading)
        {
            _reloadRequested = true;
            return;
        }

        _isLoading = true;
        var what = LoadDescription;
        try
        {
            // Never load from inside whatever asked for it. A binding can ask in the middle of a layout pass,
            // where the dispatcher is locked and a wait doesn't pump; AcTools waits for the finalizers when it
            // swaps a car (GCHelper.CleanUp), and a finalizer that needs the UI thread then hangs the game for good.
            // After the yield this runs as a dispatcher operation of its own, where the wait pumps.
            await Dispatcher.Yield(DispatcherPriority.Background);
            if (!IsLoaded || !IsVisible || _failed || IsUpToDate) return;

            what = LoadDescription;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            // Leave the viewport transparent so whatever is behind it stays visible
            Logger.Error(ex, "{Viewport} failed to load {What}", ViewportName, what);
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

    /// <summary>A renderer set up the way both viewports draw, at the control's size in pixels</summary>
    protected GarageRenderer NewRenderer(CarDescription? car, string? showroomKn5)
    {
        var (width, height) = GetPixelSize();
        return new GarageRenderer(car, showroomKn5)
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
    }

    /// <summary>
    /// Initialises a new renderer off the UI thread and puts it on screen. False when the control was unloaded in
    /// the meantime; the renderer is gone then. Throws when it cannot be started, and it is gone then too.
    /// </summary>
    protected async Task<bool> StartRendererAsync(GarageRenderer renderer)
    {
        try
        {
            await Task.Run(() => renderer.Initialize());
        }
        catch
        {
            renderer.Dispose();
            throw;
        }

        // The control may have been unloaded while the scene was loading. Check before raising a device, or one is
        // left behind on a control that will never draw again.
        if (!IsLoaded)
        {
            renderer.Dispose();
            return false;
        }

        try
        {
            // No D3D9Ex (a remote session, a driver in the middle of a reset): the renderer must not outlive this
            _bridge.EnsureDevice();
        }
        catch
        {
            renderer.Dispose();
            throw;
        }

        _renderer = renderer;
        _deviceWork = Task.CompletedTask;

        // The size was taken when the renderer was made; a resize while it was loading found no renderer to tell
        UpdateRendererSize();
        Invalidate();
        return true;
    }

    /// <summary>
    /// Device work done for the current renderer that may still be running when the control goes: a car being put
    /// in a slot ends with device calls made from a worker thread, outside the renderer's lock. The renderer waits
    /// for it before it is disposed.
    /// </summary>
    protected Task TrackDeviceWork(Task work)
    {
        _deviceWork = _deviceWork.IsCompleted ? work : Task.WhenAll(_deviceWork, work);
        return work;
    }

    #endregion

    #region Rendering

    /// <summary>
    /// Moves whatever moves by itself (the camera, mostly) by a frame. True while something is still moving, so the
    /// pump keeps drawing.
    /// </summary>
    protected abstract bool StepFrame(GarageRenderer renderer, float dt);

    /// <summary>The first frame is on screen; the viewport decides when its picture shows</summary>
    protected virtual void OnFirstFrame() { }

    /// <summary>Another frame is on screen</summary>
    protected virtual void OnFramePresented() { }

    /// <summary>The renderer went; whatever the viewport knew about its scene goes with it</summary>
    protected virtual void OnRendererDisposed() { }

    /// <summary>The picture is still held back: nothing has faded it in yet</summary>
    protected bool IsPictureHidden => _image.Opacity == 0;

    protected void FadeIn() => _image.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, FadeInDuration));

    protected void ShowPicture() => _image.Opacity = 1;

    /// <summary>Something in the scene changed: draw it, and keep drawing for the settle window</summary>
    protected void Invalidate()
    {
        if (_renderer != null) _renderer.IsDirty = true;
        AnimateFor(SettleWindow);
    }

    /// <summary>
    /// For a change made straight on the renderer that marks it dirty only when something did change (the part under
    /// the pointer): draws it at once rather than on the next look the idle clock takes
    /// </summary>
    protected void InvalidateIfDirty()
    {
        if (_renderer is { IsDirty: true }) Invalidate();
    }

    /// <summary>Keeps drawing for a while, for something that plays out by itself (doors, parts flying in)</summary>
    protected void AnimateFor(TimeSpan window)
    {
        var until = DateTime.Now + window;
        if (until > _animateUntil) _animateUntil = until;
        if (_renderer != null) _renderer.IsDirty = true;
        StartPump();
    }

    private void StartPump()
    {
        if (_renderer == null || !IsVisible) return;

        _idleTimer.Stop();
        if (_pumping) return;

        _pumping = true;
        CompositionTarget.Rendering += OnRendering;
    }

    /// <param name="idle">The scene is standing still but still on show: keep an eye out for the renderer's own changes</param>
    private void StopPump(bool idle)
    {
        if (_pumping)
        {
            _pumping = false;
            CompositionTarget.Rendering -= OnRendering;
        }

        if (idle && _renderer != null) _idleTimer.Start();
        else _idleTimer.Stop();
    }

    private void OnIdlePoll(object? sender, EventArgs e)
    {
        if (_renderer == null || !IsVisible)
        {
            _idleTimer.Stop();
            return;
        }

        if (_renderer.IsDirty) StartPump();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // CompositionTarget.Rendering can fire more than once per frame
        var args = (RenderingEventArgs)e;
        if (args.RenderingTime == _lastRenderingTime) return;

        var dt = (float)(args.RenderingTime - _lastRenderingTime).TotalSeconds;
        _lastRenderingTime = args.RenderingTime;
        if (dt <= 0f || dt > 0.25f) dt = 1f / 60f;

        var renderer = _renderer;
        if (renderer == null || !IsVisible || !_bridge.IsFrontBufferAvailable)
        {
            // Coming back into view, or the front buffer coming back, starts it again
            StopPump(idle: false);
            return;
        }

        var moving = StepFrame(renderer, dt);

        var now = DateTime.Now;
        if (renderer.IsDirty && _animateUntil < now + SettleWindow)
        {
            _animateUntil = now + SettleWindow;
        }

        if (!moving && now >= _animateUntil && _bridge.BoundTarget != IntPtr.Zero)
        {
            StopPump(idle: true);
            return;
        }

        try
        {
            renderer.Draw();
            _bridge.Present(renderer.GetRenderTarget());

            if (!IsReady)
            {
                IsReady = true;
                OnFirstFrame();
                Ready?.Invoke(this, EventArgs.Empty);
            }

            OnFramePresented();

            // A scene that has drawn well for a while since it was rebuilt has earned another rebuild: a device lost
            // again soon after is still a failure, so this cannot loop
            if (_recovered && DateTime.Now - _recoveredAt >= RecoveryGrace) _recovered = false;
        }
        catch (Exception ex) when (!_recovered && IsDeviceLost(ex))
        {
            // The GPU went away under the renderer (a driver update, a reset). Everything on it is gone, the scene
            // included, so it is built again from scratch, once: a device that keeps going is a failure after all.
            Logger.Warning(ex, "{Viewport}: the graphics device was lost, starting the scene again", ViewportName);
            _recovered = true;
            _recoveredAt = DateTime.Now;
            DisposeRenderer(releaseDevice: true);
            Dispatcher.InvokeAsync(RequestLoad);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "{Viewport} render loop failed", ViewportName);
            Fail();
        }
    }

    private static bool IsDeviceLost(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            var code = e is SlimDX.SlimDXException slim ? slim.ResultCode.Code : e.HResult;
            if (DeviceLostCodes.Contains(code)) return true;
        }

        return false;
    }

    protected (int Width, int Height) GetPixelSize()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = Math.Max(16, (int)Math.Round(ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(16, (int)Math.Round(ActualHeight * dpi.DpiScaleY));
        return (width, height);
    }

    /// <summary>Moved to a monitor of other scaling: the same size in DIPs is another size in pixels</summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        UpdateRendererSize();
    }

    private void UpdateRendererSize()
    {
        var renderer = _renderer;
        if (renderer == null) return;

        var (width, height) = GetPixelSize();
        if (renderer.Width == width && renderer.Height == height) return;

        renderer.Width = width;
        renderer.Height = height;
        Invalidate();
    }

    /// <summary>
    /// Takes the renderer off screen and disposes it, once any device work still running on it is done.
    /// <paramref name="releaseDevice"/> also lets go of the bridge's D3D9 device; the next renderer raises it again.
    /// </summary>
    protected void DisposeRenderer(bool releaseDevice)
    {
        StopPump(idle: false);
        _resizeTimer.Stop();
        _rock = null;

        var renderer = _renderer;
        _renderer = null;

        // The target belongs to the renderer: let go of it before the renderer goes
        _bridge.ReleaseSurface();

        if (renderer != null)
        {
            var work = _deviceWork;
            _deviceWork = Task.CompletedTask;
            _ = DisposeAfterAsync(renderer, work);
        }

        if (releaseDevice) _bridge.Dispose();

        OnRendererDisposed();

        IsReady = false;
        ShowLabel(null, default);
        _image.BeginAnimation(OpacityProperty, null);
        _image.Opacity = 0;
    }

    private async Task DisposeAfterAsync(GarageRenderer renderer, Task work)
    {
        try
        {
            await work;
        }
        catch
        {
            // Whoever started the work has logged how it ended; all that matters here is that it has
        }

        try
        {
            renderer.Dispose();
        }
        catch (Exception ex)
        {
            // A renderer on a device that is gone can fail on the way out too; nothing is left to be done with it
            Logger.Warning(ex, "{Viewport}: the renderer did not dispose cleanly", ViewportName);
        }
    }

    protected void Fail()
    {
        _failed = true;
        DisposeRenderer(releaseDevice: true);
        HasFailed = true;
        Failed?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Label

    /// <summary>A label next to the pointer; null takes it away</summary>
    protected void ShowLabel(string? text, System.Windows.Point position)
    {
        if (text == null)
        {
            _label.Visibility = Visibility.Collapsed;
            return;
        }

        _labelText.Text = text;
        _label.Margin = new Thickness(position.X + _labelOffset.X, position.Y + _labelOffset.Y, 0, 0);
        _label.Visibility = Visibility.Visible;
    }

    #endregion

    #region Body rock

    /// <summary>Only the garage leans the parts it shows along with the body</summary>
    protected virtual bool LeansParts => false;

    private void OnRunningEngineChanged(Audio.EngineRunner? runner)
    {
        // Only while on screen: a runner that outlives the control must not keep it alive through the event
        if (IsLoaded) SetPoseSource(runner);
        OnPoseChanged();
    }

    private void SetPoseSource(Audio.EngineRunner? runner)
    {
        if (ReferenceEquals(runner, _poseSource)) return;

        if (_poseSource != null) _poseSource.PoseChanged -= OnPoseChanged;
        _poseSource = runner;
        if (_poseSource != null) _poseSource.PoseChanged += OnPoseChanged;
    }

    /// <summary>The engine moved the body, or the engine changed: the viewport says which car it rocks</summary>
    protected abstract void OnPoseChanged();

    /// <summary>Rocks <paramref name="carNode"/> the way the running engine has it; null or an engine at rest lets it settle</summary>
    protected void RockCar(Kn5RenderableCar? carNode)
    {
        var renderer = _renderer;
        var pose = RunningEngine?.Pose ?? Audio.BodyPose.Rest;
        if (renderer == null || carNode == null || (pose.IsRest && RunningEngine?.IsActive != true))
        {
            ReleaseRock();
            return;
        }

        if (_rock == null || !ReferenceEquals(_rock.Car, carNode))
        {
            ReleaseRock();
            _rock = new CarBodyRock(carNode);
        }

        if (!_rock.Apply(pose)) return;

        if (LeansParts) renderer.BodyLean = _rock.WorldLean;
        renderer.RefreshShadows();
        Invalidate();
    }

    /// <summary>Puts the rocked car back on its wheels</summary>
    protected void ReleaseRock()
    {
        if (_rock == null) return;

        _rock.Release();
        _rock = null;
        if (_renderer != null)
        {
            if (LeansParts) _renderer.BodyLean = SlimDX.Matrix.Identity;
            _renderer.RefreshShadows();
            _renderer.IsDirty = true;
            StartPump();
        }
    }

    #endregion
}
