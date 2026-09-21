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
using Street_Rod_AC.Parts.Logic;
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
    private InstalledPart? _partsEngine;
    private bool _isLoadingCandidates;

    // What the part models on screen are made of: node index to part, and where every part sits
    private IReadOnlyList<PlacedPart> _partNodes = Array.Empty<PlacedPart>();
    private Dictionary<InstalledPart, System.Numerics.Matrix4x4> _partWorlds = new();
    private IReadOnlyList<MountCandidate> _candidateNodes = Array.Empty<MountCandidate>();
    private CarAnchors? _anchors;

    // A press that does not turn into a drag is a click
    private const double ClickSlack = 4.0;
    private static readonly TimeSpan HoverInterval = TimeSpan.FromMilliseconds(30);
    private System.Windows.Point _pressedAt;
    private DateTime _lastHoverPick = DateTime.MinValue;
    private readonly System.Windows.Controls.Border _partLabel;
    private readonly System.Windows.Controls.TextBlock _partLabelText;

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

        // Name of the part under the pointer, next to the pointer
        _partLabelText = new System.Windows.Controls.TextBlock
        {
            Foreground = System.Windows.Media.Brushes.White,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold
        };
        _partLabel = new System.Windows.Controls.Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xD0, 0x10, 0x10, 0x10)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x80, 0xFF, 0xE6, 0x8C)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 4, 8, 4),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Child = _partLabelText
        };
        Children.Add(_partLabel);

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

    /// <summary>A mounted part was clicked in the parts view; null for a click on nothing</summary>
    public event Action<InstalledPart?>? PartClicked;

    /// <summary>One of the places shown for a loose part was clicked</summary>
    public event Action<MountCandidate>? CandidateClicked;

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

    public static readonly DependencyProperty PartsCatalogProperty = DependencyProperty.Register(
        nameof(PartsCatalog), typeof(PartsCatalog), typeof(CarViewport3D),
        new PropertyMetadata(null, (d, _) => ((CarViewport3D)d).ApplyParts()));

    /// <summary>Where the part models come from; without it the parts view stays off</summary>
    public PartsCatalog? PartsCatalog
    {
        get => (PartsCatalog?)GetValue(PartsCatalogProperty);
        set => SetValue(PartsCatalogProperty, value);
    }

    public static readonly DependencyProperty EngineProperty = DependencyProperty.Register(
        nameof(Engine), typeof(InstalledPart), typeof(CarViewport3D),
        new PropertyMetadata(null, (d, _) => ((CarViewport3D)d).ApplyParts()));

    /// <summary>
    /// The car's engine block with everything that is on it; null for an empty engine bay.
    /// The tree is drawn as it is when set: after a change, set a new tree.
    /// </summary>
    public InstalledPart? Engine
    {
        get => (InstalledPart?)GetValue(EngineProperty);
        set => SetValue(EngineProperty, value);
    }

    public static readonly DependencyProperty CandidatesProperty = DependencyProperty.Register(
        nameof(Candidates), typeof(IReadOnlyList<MountCandidate>), typeof(CarViewport3D),
        new PropertyMetadata(null, (d, _) => ((CarViewport3D)d).ApplyCandidates()));

    /// <summary>A loose part in every place it could go, shown glowing; a click on one raises <see cref="CandidateClicked"/></summary>
    public IReadOnlyList<MountCandidate>? Candidates
    {
        get => (IReadOnlyList<MountCandidate>?)GetValue(CandidatesProperty);
        set => SetValue(CandidatesProperty, value);
    }

    public static readonly DependencyProperty SelectedPartProperty = DependencyProperty.Register(
        nameof(SelectedPart), typeof(InstalledPart), typeof(CarViewport3D),
        new PropertyMetadata(null, (d, _) => ((CarViewport3D)d).ApplySelection()));

    /// <summary>The mounted part that is marked as selected</summary>
    public InstalledPart? SelectedPart
    {
        get => (InstalledPart?)GetValue(SelectedPartProperty);
        set => SetValue(SelectedPartProperty, value);
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
        var catalog = PartsCatalog;
        if (!PartsVisible || carNode == null || catalog == null || string.IsNullOrEmpty(carDirectory))
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

            renderer.SetCandidates(null);
            _partsCarDirectory = null;
            _partsEngine = null;
            ShowPartLabel(null, default);
            _animateUntil = DateTime.Now + AnimationWindow;
            return;
        }

        // The layout depends on the car and on what is in it, so either changing rebuilds it
        var engine = Engine;
        if (renderer.HasProp && _partsCarDirectory == carDirectory && ReferenceEquals(_partsEngine, engine)) return;

        var anchors = GetAnchors(carNode);
        if (anchors == null)
        {
            _logger.Warning("Car has no wheel nodes to place the parts by: {Car}", carDirectory);
            return;
        }

        _isLoadingParts = true;
        try
        {
            var started = DateTime.Now;

            // Same car, other parts: what is no longer there lifts off while the new model is put together
            var sameCar = renderer.HasProp && _partsCarDirectory == carDirectory;
            var before = sameCar ? IdsOf(_partNodes) : null;
            var after = engine?.SelfAndDescendants().Select(p => p.InstanceId).Where(id => id != Guid.Empty).ToHashSet() ?? new HashSet<Guid>();
            var leaving = before == null ? new List<int>() : NodesWhere(_partNodes, id => !after.Contains(id));
            if (leaving.Count > 0)
            {
                renderer.MoveParts(leaving, arriving: false);
                _animateUntil = DateTime.Now + AnimationWindow;
            }

            var (placed, model) = await Task.Run(() =>
            {
                var parts = CarPartsLayout.Build(catalog, anchors, engine);
                return (parts, PartAssembler.BuildModel(catalog, "parts", parts));
            });

            // The renderer only moves things while it draws: never wait on it for long
            for (var waited = 0; waited < 1500 && renderer == _renderer && renderer.IsMovingParts; waited += 30) await Task.Delay(30);

            // The renderer or the car may have been replaced, or the toggle flipped back, while loading
            if (renderer == _renderer && PartsVisible && _loadedCarDirectory == carDirectory)
            {
                renderer.SetProp(model.Kn5, SlimDX.Matrix.Identity);

                // And what is new comes flying in
                if (before != null) renderer.MoveParts(NodesWhere(model.Nodes, id => !before.Contains(id)), arriving: true);
                renderer.GhostCar = true;
                _partsCarDirectory = carDirectory;
                _partsEngine = engine;
                _anchors = anchors;
                _partNodes = model.Nodes;
                _partWorlds = placed.Where(p => p.Source != null).ToDictionary(p => p.Source!, p => p.World);
                _animateUntil = DateTime.Now + AnimationWindow;

                _logger.Information("Parts of {Car} laid out in {Ms} ms ({Count} parts, engine {Engine})", Path.GetFileName(carDirectory),
                    (int)(DateTime.Now - started).TotalMilliseconds, placed.Count, engine?.Definition.Id ?? "none");

                ApplySelection();
                ApplyCandidates();
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
        if (_renderer != null && (!PartsVisible || _loadedCarDirectory != carDirectory || !ReferenceEquals(Engine, engine))) ApplyParts();
    }

    private static HashSet<Guid> IdsOf(IReadOnlyList<PlacedPart> nodes) =>
        nodes.Select(n => n.Source?.InstanceId ?? Guid.Empty).Where(id => id != Guid.Empty).ToHashSet();

    /// <summary>Nodes of the parts somebody owns whose id passes the test; the fixed running gear has no id and never moves</summary>
    private static List<int> NodesWhere(IReadOnlyList<PlacedPart> nodes, Func<Guid, bool> test) =>
        Enumerable.Range(0, nodes.Count)
            .Where(i => nodes[i].Source is { } source && source.InstanceId != Guid.Empty && test(source.InstanceId))
            .ToList();

    /// <summary>Shows the loose part of <see cref="Candidates"/> in the places it could go</summary>
    private async void ApplyCandidates()
    {
        var renderer = _renderer;
        if (renderer == null || _isLoadingCandidates) return;

        var candidates = Candidates;
        var catalog = PartsCatalog;
        var anchors = _anchors;
        if (candidates == null || candidates.Count == 0 || catalog == null || anchors == null || !renderer.HasProp)
        {
            renderer.SetCandidates(null);
            _candidateNodes = Array.Empty<MountCandidate>();
            return;
        }

        _isLoadingCandidates = true;
        try
        {
            var worlds = _partWorlds;
            var (model, owners) = await Task.Run(() =>
            {
                var placed = new List<PlacedPart>();
                var ownerOf = new Dictionary<InstalledPart, MountCandidate>();
                foreach (var candidate in candidates)
                {
                    var world = candidate.Parent == null
                        ? CarPartsLayout.EnginePlacement(anchors, candidate.Part.Definition)
                        : worlds.TryGetValue(candidate.Parent, out var parentWorld)
                            ? PartAssembler.ChildWorld(candidate.Parent.Definition, candidate.ParentSlot, candidate.Part.Definition, candidate.OwnSlot, parentWorld)
                            : null;
                    if (world == null) continue;

                    foreach (var part in PartAssembler.Assemble(candidate.Part, world.Value))
                    {
                        placed.Add(part);
                        ownerOf[part.Source!] = candidate;
                    }
                }

                if (placed.Count == 0) return (null, Array.Empty<MountCandidate>());

                var built = PartAssembler.BuildModel(catalog, "candidates", placed);
                return (built, built.Nodes.Select(n => ownerOf[n.Source!]).ToArray());
            });

            if (renderer == _renderer && ReferenceEquals(candidates, Candidates))
            {
                renderer.SetCandidates(model?.Kn5);
                _candidateNodes = owners;
                _animateUntil = DateTime.Now + AnimationWindow;
            }
        }
        catch (InvalidOperationException)
        {
            // None of the parts has a model: nothing to show, nothing to click
            renderer.SetCandidates(null);
            _candidateNodes = Array.Empty<MountCandidate>();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not show where the part goes");
        }
        finally
        {
            _isLoadingCandidates = false;
        }

        if (_renderer != null && !ReferenceEquals(candidates, Candidates)) ApplyCandidates();
    }

    private void ApplySelection()
    {
        if (_renderer == null) return;

        var selected = SelectedPart;
        var node = -1;
        for (var i = 0; selected != null && i < _partNodes.Count; i++)
        {
            if (ReferenceEquals(_partNodes[i].Source, selected)) node = i;
        }

        _renderer.SelectedPart = node < 0 ? null : node;
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
        _lastMouse = _pressedAt = e.GetPosition(this);
        CaptureMouse();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var wasDragging = _isDragging;
        _isDragging = false;
        ReleaseMouseCapture();

        var position = e.GetPosition(this);
        if (wasDragging && (position - _pressedAt).Length <= ClickSlack) OnClick(position);
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var orbit = _renderer?.CameraOrbit;
        if (orbit == null) return;

        var position = e.GetPosition(this);
        if (!_isDragging)
        {
            UpdateHover(position);
            return;
        }

        orbit.Alpha += (float)(position.X - _lastMouse.X) * 0.01f;
        orbit.Beta = Math.Clamp(orbit.Beta + (float)(position.Y - _lastMouse.Y) * 0.01f, MinBeta, MaxBeta);
        _lastMouse = position;

        if ((position - _pressedAt).Length > ClickSlack) ShowPartLabel(null, default);
        _renderer!.IsDirty = true;
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_renderer != null) _renderer.HoveredPart = null;
        ShowPartLabel(null, default);
    }

    private PartHit? PickAt(System.Windows.Point position) =>
        _renderer == null || ActualWidth <= 0 || ActualHeight <= 0
            ? null
            : _renderer.Pick((float)(position.X / ActualWidth), (float)(position.Y / ActualHeight));

    private void UpdateHover(System.Windows.Point position)
    {
        if (_renderer is not { HasProp: true } || !PartsVisible) return;

        // Picking tests triangles on the CPU; the pointer moves more often than it needs to be asked
        var now = DateTime.Now;
        if (now - _lastHoverPick < HoverInterval) return;
        _lastHoverPick = now;

        var hit = PickAt(position);
        _renderer.HoveredPart = hit;
        ShowPartLabel(hit == null ? null : DefinitionOf(hit.Value), position);
    }

    private void OnClick(System.Windows.Point position)
    {
        if (_renderer is not { HasProp: true } || !PartsVisible) return;

        var hit = PickAt(position);
        if (hit is { Layer: PartLayer.Candidate } candidate && candidate.Node < _candidateNodes.Count)
        {
            CandidateClicked?.Invoke(_candidateNodes[candidate.Node]);
            return;
        }

        var part = hit is { Layer: PartLayer.Mounted } mounted && mounted.Node < _partNodes.Count ? _partNodes[mounted.Node].Source : null;
        PartClicked?.Invoke(part);
    }

    private PartDefinition? DefinitionOf(PartHit hit) => hit.Layer switch
    {
        PartLayer.Mounted when hit.Node < _partNodes.Count => _partNodes[hit.Node].Part,
        PartLayer.Candidate when hit.Node < _candidateNodes.Count => _candidateNodes[hit.Node].Part.Definition,
        _ => null
    };

    private void ShowPartLabel(PartDefinition? part, System.Windows.Point position)
    {
        if (part == null)
        {
            _partLabel.Visibility = Visibility.Collapsed;
            return;
        }

        _partLabelText.Text = part.DisplayName ?? part.Name;
        _partLabel.Margin = new Thickness(position.X + 18, position.Y + 14, 0, 0);
        _partLabel.Visibility = Visibility.Visible;
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
