using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AcTools.Render.Kn5Specific.Objects;
using AcTools.Render.Kn5SpecificForwardDark;
using Street_Rod_AC.Configuration;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Parts;
using Street_Rod_AC.Parts.Logic;

namespace Street_Rod_AC.Controls;

/// <summary>
/// Real-time 3D car viewport backed by the AcTools renderer.
/// The renderer draws off-screen into a shared DX11 texture which is presented through a D3DImage,
/// so the viewport composes like any other WPF element (overlays, opacity, dialogs on top all work).
/// </summary>
public class CarViewport3D : D3DViewportBase
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

    /// <summary>
    /// How quickly the camera settles on a new framing, as the share of the remaining distance it covers per
    /// second. The first car of a session is put in place at once; after that the camera moves rather than
    /// cuts, so changing car or opening the workbench reads as the camera going somewhere.
    /// </summary>
    private const float CameraSettleRate = 2.6f;
    private const float CameraSettled = 0.004f;

    // Keep drawing for a while after a toggle so door/light animations play out
    private static readonly TimeSpan AnimationWindow = TimeSpan.FromSeconds(3);

    // Where the camera is headed, eased toward each frame
    private float _radiusGoal = DefaultRadius;
    private float _alphaGoal = DefaultAlpha;
    private float _betaGoal = DefaultBeta;
    private SlimDX.Vector3? _targetGoal;

    private string? _loadedCarDirectory;
    private System.Windows.Point _lastMouse;
    private bool _isDragging;
    private bool _isLoadingParts;
    private bool _applyPartsQueued;
    private string? _partsCarDirectory;
    private InstalledPart? _partsEngine;
    private IReadOnlyList<CarPart>? _partsGear;
    private bool _isLoadingCandidates;

    // What the part models on screen are made of: node index to part, and where every part sits
    private IReadOnlyList<PlacedPart> _partNodes = Array.Empty<PlacedPart>();
    private Dictionary<InstalledPart, System.Numerics.Matrix4x4> _partWorlds = new();
    private IReadOnlyList<MountCandidate> _candidateNodes = Array.Empty<MountCandidate>();
    private IReadOnlyList<MountCandidate>? _shownCandidates;
    private CarAnchors? _anchors;

    // A press that does not turn into a drag is a click
    private const double ClickSlack = 4.0;
    private static readonly TimeSpan HoverInterval = TimeSpan.FromMilliseconds(30);
    private System.Windows.Point _pressedAt;
    private DateTime _lastHoverPick = DateTime.MinValue;

    public CarViewport3D()
        : base("Viewport3D", new LabelLook(13, 0xD0, 0x80, new Thickness(8, 4, 8, 4), new Vector(18, 14)))
    {
    }

    protected override string ViewportName => "3D viewport";

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
        new PropertyMetadata(false, (d, _) => ((CarViewport3D)d).QueueApplyParts()));

    /// <summary>X-ray view: the car fades to a shell and its parts show in their places</summary>
    public bool PartsVisible
    {
        get => (bool)GetValue(PartsVisibleProperty);
        set => SetValue(PartsVisibleProperty, value);
    }

    public static readonly DependencyProperty PartsCatalogProperty = DependencyProperty.Register(
        nameof(PartsCatalog), typeof(PartsCatalog), typeof(CarViewport3D),
        new PropertyMetadata(null, (d, _) => ((CarViewport3D)d).QueueApplyParts()));

    /// <summary>Where the part models come from; without it the parts view stays off</summary>
    public PartsCatalog? PartsCatalog
    {
        get => (PartsCatalog?)GetValue(PartsCatalogProperty);
        set => SetValue(PartsCatalogProperty, value);
    }

    public static readonly DependencyProperty EngineProperty = DependencyProperty.Register(
        nameof(Engine), typeof(InstalledPart), typeof(CarViewport3D),
        new PropertyMetadata(null, (d, _) => ((CarViewport3D)d).QueueApplyParts()));

    /// <summary>
    /// The car's engine block with everything that is on it; null for an empty engine bay.
    /// The tree is drawn as it is when set: after a change, set a new tree.
    /// </summary>
    public InstalledPart? Engine
    {
        get => (InstalledPart?)GetValue(EngineProperty);
        set => SetValue(EngineProperty, value);
    }

    public static readonly DependencyProperty GearProperty = DependencyProperty.Register(
        nameof(Gear), typeof(IReadOnlyList<CarPart>), typeof(CarViewport3D),
        new PropertyMetadata(null, (d, _) => ((CarViewport3D)d).QueueApplyParts()));

    /// <summary>What sits on the car's wheel slots, each with what is on it; null or empty for bare hubs</summary>
    public IReadOnlyList<CarPart>? Gear
    {
        get => (IReadOnlyList<CarPart>?)GetValue(GearProperty);
        set => SetValue(GearProperty, value);
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

    // An empty directory means "no car": only the showroom is rendered
    private string RequestedCarDirectory => CarDirectory ?? string.Empty;

    protected override bool IsUpToDate => Renderer != null && RequestedCarDirectory == _loadedCarDirectory;

    protected override string LoadDescription => RequestedCarDirectory;

    protected override async Task LoadAsync()
    {
        var carDirectory = RequestedCarDirectory;
        var hasCar = carDirectory.Length > 0;
        if (hasCar && !Directory.Exists(carDirectory))
            throw new DirectoryNotFoundException($"Car folder not found: {carDirectory}");

        var started = DateTime.Now;
        var car = hasCar ? CarDescription.FromDirectory(carDirectory) : null;
        var skinId = SkinId;

        var renderer = Renderer;
        if (renderer == null)
        {
            var showroom = ShowroomKn5;
            if (showroom != null && !File.Exists(showroom))
            {
                Logger.Warning("Showroom not found, rendering without it: {Showroom}", showroom);
                showroom = null;
            }

            if (car == null && showroom == null)
                throw new InvalidOperationException("Nothing to render: no car and no showroom");

            renderer = NewRenderer(car, showroom);
            if (!await StartRendererAsync(renderer)) return;

            ResetCamera(immediate: true);
        }
        else
        {
            ReleaseRock();
            await TrackDeviceWork(renderer.MainSlot.SetCarAsync(car, skinId ?? Kn5RenderableCar.DefaultSkin));

            // The control may have been unloaded while the car was loading, and the renderer is gone then
            if (renderer != Renderer) return;

            ResetCamera();
        }

        _loadedCarDirectory = carDirectory;
        HasCar = renderer.CarNode != null;
        HasDoors = renderer.CarNode?.HasLeftDoorAnimation == true || renderer.CarNode?.HasRightDoorAnimation == true;
        ApplySkin();
        ApplyCarState();
        ApplyParts();
        Invalidate();

        Logger.Information("Loaded {Car} in {Ms} ms", hasCar ? Path.GetFileName(carDirectory) : "(empty garage)",
            (int)(DateTime.Now - started).TotalMilliseconds);
    }

    /// <summary>
    /// Frames the car. <paramref name="immediate"/> puts the camera there at once, which is right for the
    /// first car of a session; otherwise it moves there over the next moment.
    /// </summary>
    private void ResetCamera(bool immediate = false)
    {
        var orbit = Renderer?.CameraOrbit;
        if (orbit == null) return;

        _radiusGoal = DefaultRadius;
        _alphaGoal = DefaultAlpha;
        _betaGoal = DefaultBeta;
        _targetGoal = null;

        if (Renderer!.CarNode == null)
        {
            // Empty garage: nothing to frame, so look across the room at eye level instead of at the floor
            Renderer.AutoAdjustTarget = false;
            _targetGoal = new SlimDX.Vector3(0f, EmptyGarageEyeHeight, 0f);
            _radiusGoal = MaxRadius;
            _betaGoal = MinBeta;
        }
        else
        {
            Renderer.AutoAdjustTarget = true;
        }

        if (immediate)
        {
            orbit.Radius = _radiusGoal;
            orbit.Alpha = _alphaGoal;
            orbit.Beta = _betaGoal;
            if (_targetGoal is { } target) orbit.Target = target;
        }

        AnimateFor(AnimationWindow);
    }

    /// <summary>
    /// Eases the camera toward its framing. Returns true while it is still moving, so the render loop knows
    /// to keep drawing. A share of what is left every second, so it comes out the same at any frame rate.
    /// </summary>
    private bool StepCamera(float dt)
    {
        var orbit = Renderer?.CameraOrbit;
        if (orbit == null) return false;

        var k = 1f - (float)Math.Exp(-CameraSettleRate * dt);
        var moving = false;

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

        if (Renderer!.AutoRotate)
        {
            // The renderer is turning the car itself, a little further every tick. Easing alpha back to a
            // goal at the same time would cancel that out and leave the car shivering on the spot instead of
            // going round, so the goal follows the renderer until auto-rotate is switched off.
            _alphaGoal = orbit.Alpha;
        }
        else
        {
            // Round the short way
            var delta = (float)Math.IEEERemainder(_alphaGoal - orbit.Alpha, Math.PI * 2);
            if (Math.Abs(delta) > CameraSettled)
            {
                orbit.Alpha += delta * k;
                moving = true;
            }
        }

        if (_targetGoal is { } target)
        {
            var toTarget = target - orbit.Target;
            if (toTarget.LengthSquared() > CameraSettled * CameraSettled)
            {
                orbit.Target += toTarget * k;
                moving = true;
            }
        }

        return moving;
    }

    /// <summary>
    /// The bindings land one at a time (the catalog, then the engine, then the gear, each a change of its own),
    /// so the layout waits for the lot and is built once
    /// </summary>
    private void QueueApplyParts()
    {
        if (_applyPartsQueued) return;

        _applyPartsQueued = true;
        Dispatcher.InvokeAsync(() =>
        {
            _applyPartsQueued = false;
            ApplyParts();
        });
    }

    /// <summary>
    /// Builds the parts view for what is bound now. Runs from property callbacks, so nothing may escape it: a throw
    /// anywhere in it is logged and the garage goes on without the parts view.
    /// </summary>
    private async void ApplyParts()
    {
        try
        {
            await ApplyPartsAsync();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not lay out the parts");
        }
    }

    private async Task ApplyPartsAsync()
    {
        var renderer = Renderer;
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
            _partsGear = null;
            ShowPartLabel(null, default);
            AnimateFor(AnimationWindow);
            return;
        }

        // The layout depends on the car and on what is in it, so either changing rebuilds it
        var engine = Engine;
        var gear = Gear;
        if (renderer.HasProp && _partsCarDirectory == carDirectory && ReferenceEquals(_partsEngine, engine) && ReferenceEquals(_partsGear, gear)) return;

        var anchors = GetAnchors(carNode);
        if (anchors == null)
        {
            Logger.Warning("Car has no wheel nodes to place the parts by: {Car}", carDirectory);
            return;
        }

        _isLoadingParts = true;
        try
        {
            var started = DateTime.Now;

            // Same car, other parts: what is no longer there lifts off while the new model is put together
            var sameCar = renderer.HasProp && _partsCarDirectory == carDirectory;
            var before = sameCar ? IdsOf(_partNodes) : null;
            var roots = (gear ?? Array.Empty<CarPart>()).Select(g => g.Root).Concat(engine == null ? Array.Empty<InstalledPart>() : new[] { engine });
            var after = roots.SelectMany(r => r.SelfAndDescendants()).Select(p => p.InstanceId).Where(id => id != Guid.Empty).ToHashSet();
            var leaving = before == null ? new List<int>() : NodesWhere(_partNodes, id => !after.Contains(id));
            if (leaving.Count > 0)
            {
                renderer.MoveParts(leaving, arriving: false);
                AnimateFor(AnimationWindow);
            }

            var (placed, model) = await Task.Run(() =>
            {
                var parts = CarPartsLayout.Build(catalog, anchors, engine, gear);
                return (parts, PartAssembler.BuildModel(catalog, "parts", parts));
            });

            // The renderer only moves things while it draws: never wait on it for long
            for (var waited = 0; waited < 1500 && renderer == Renderer && renderer.IsMovingParts; waited += 30) await Task.Delay(30);

            // The renderer or the car may have been replaced, or the toggle flipped back, while loading
            if (renderer == Renderer && PartsVisible && _loadedCarDirectory == carDirectory)
            {
                renderer.SetProp(model.Kn5, SlimDX.Matrix.Identity);

                // And what is new comes flying in
                if (before != null) renderer.MoveParts(NodesWhere(model.Nodes, id => !before.Contains(id)), arriving: true);
                renderer.GhostCar = true;
                _partsCarDirectory = carDirectory;
                _partsEngine = engine;
                _partsGear = gear;
                _anchors = anchors;
                _partNodes = model.Nodes;
                _partWorlds = placed.Where(p => p.Source != null).ToDictionary(p => p.Source!, p => p.World);
                AnimateFor(AnimationWindow);

                // The part being placed is gone, or the tree was made anew: the mode is over
                if (_placing != null && !_partWorlds.ContainsKey(_placing))
                {
                    _placing = null;
                    PlacementChanged?.Invoke(null);
                }

                Logger.Information("Parts of {Car} laid out in {Ms} ms ({Count} parts, engine {Engine})", Path.GetFileName(carDirectory),
                    (int)(DateTime.Now - started).TotalMilliseconds, placed.Count, engine?.Definition.Id ?? "none");

                ApplySelection();
                ApplyCandidates();
            }
        }
        catch (Exception ex)
        {
            // The garage works fine without the parts view, so just leave it off
            Logger.Error(ex, "Could not lay out the parts");
        }
        finally
        {
            _isLoadingParts = false;
        }

        // Catch up with whatever changed in the meantime, a renderer rebuilt under it included (device lost, or a
        // quick unload and load): its own load found this run in progress and left the parts to it
        if (Renderer != null && (renderer != Renderer || !PartsVisible || _loadedCarDirectory != carDirectory || !ReferenceEquals(Engine, engine) || !ReferenceEquals(Gear, gear))) ApplyParts();
    }

    private static HashSet<Guid> IdsOf(IReadOnlyList<PlacedPart> nodes) =>
        nodes.Select(n => n.Source?.InstanceId ?? Guid.Empty).Where(id => id != Guid.Empty).ToHashSet();

    /// <summary>Nodes of the parts somebody owns whose id passes the test; the fixed running gear has no id and never moves</summary>
    private static List<int> NodesWhere(IReadOnlyList<PlacedPart> nodes, Func<Guid, bool> test) =>
        Enumerable.Range(0, nodes.Count)
            .Where(i => nodes[i].Source is { } source && source.InstanceId != Guid.Empty && test(source.InstanceId))
            .ToList();

    #region Placement mode

    /// <summary>What a nudge in placement mode moves: the part's own mounting slot, or the slot it sits on</summary>
    public enum PlacementTarget { Part, Pad }

    /// <summary>Told what the placement mode is doing, for the screen to show; null when the mode is off</summary>
    public event Action<string?>? PlacementChanged;

    private InstalledPart? _placing;
    private PlacementTarget _placementTarget;

    public bool IsPlacing => _placing != null;

    /// <summary>
    /// Enters placement mode on the selected part: nudges move it (or the pad it sits on) and are written to the
    /// catalog's slot shifts. Leaves the mode when there is no selected part or it is already on.
    /// </summary>
    public void TogglePlacement()
    {
        if (_placing != null || SelectedPart is not { Parent: not null } part || PartsCatalog == null)
        {
            _placing = null;
            PlacementChanged?.Invoke(null);
            return;
        }

        // A part's own slot is the part's wherever it goes: nudging it moves every copy, the two carburettors of a
        // dual quad along with each other. One of several on the same parent is placed by its pad, which is its own
        _placing = part;
        _placementTarget = IsOneOfSeveral(part) ? PlacementTarget.Pad : PlacementTarget.Part;
        ReportPlacement();
    }

    /// <summary>True when the parent holds the same part on another slot too (a set of carburettors, their filters)</summary>
    private static bool IsOneOfSeveral(InstalledPart part) =>
        part.Parent != null && part.Parent.Children.Values.Count(c => c.Definition.Id.Equals(part.Definition.Id, StringComparison.OrdinalIgnoreCase)) > 1;

    public void TogglePlacementTarget()
    {
        if (_placing == null) return;

        _placementTarget = _placementTarget == PlacementTarget.Part ? PlacementTarget.Pad : PlacementTarget.Part;
        ReportPlacement();
    }

    /// <summary>
    /// Moves the placed part by a step in the engine's axes: right, up, and forward (towards the radiator). The
    /// step becomes a move of a slot in that slot's own part's space, so it is what the packs store.
    /// </summary>
    public void NudgePlacement(float right, float up, float forward)
    {
        var catalog = PartsCatalog;
        if (_placing is not { Parent: { } parent } part || catalog == null || Engine == null) return;
        if (!_partWorlds.TryGetValue(Engine, out var engineWorld) || !_partWorlds.TryGetValue(part, out var partWorld)
            || !_partWorlds.TryGetValue(parent, out var parentWorld)) return;

        // SLRR parts look down their +Z at the firewall: forward is -Z of the engine
        var engineAxes = engineWorld;
        engineAxes.Translation = System.Numerics.Vector3.Zero;
        var worldDelta = System.Numerics.Vector3.TransformNormal(new System.Numerics.Vector3(right, up, -forward), engineAxes);

        // The part hangs so that its own slot lands on the pad: moving its slot moves the part the other way
        var (definition, slotId) = PlacedSlot(part, parent);
        var (frame, sign) = _placementTarget == PlacementTarget.Part ? (partWorld, -1f) : (parentWorld, 1f);
        frame.Translation = System.Numerics.Vector3.Zero;
        if (!System.Numerics.Matrix4x4.Invert(frame, out var toLocal)) return;
        var local = System.Numerics.Vector3.TransformNormal(worldDelta, toLocal) * sign;

        Shift(catalog, definition, slotId, new[] { local.X, local.Y, local.Z });
    }

    /// <summary>Takes the slot being placed back to where the packs put it</summary>
    public void ResetPlacement()
    {
        var catalog = PartsCatalog;
        if (_placing is not { Parent: { } parent } part || catalog == null) return;

        var (definition, slotId) = PlacedSlot(part, parent);
        var offset = catalog.Shifts.Of(definition.Id, slotId);
        Shift(catalog, definition, slotId, new[] { -offset[0], -offset[1], -offset[2] });
    }

    /// <summary>The slot a nudge moves: the part's own mounting slot, or the pad of its parent it sits on</summary>
    private (PartDefinition Definition, int SlotId) PlacedSlot(InstalledPart part, InstalledPart parent) =>
        _placementTarget == PlacementTarget.Part ? (part.Definition, part.OwnSlot) : (parent.Definition, part.ParentSlot);

    private void Shift(PartsCatalog catalog, PartDefinition definition, int slotId, float[] delta)
    {
        if (!catalog.ShiftSlot(definition, slotId, delta))
            Logger.Warning("Slot {Slot} of {Part} moved in the garage, but the move could not be written to {File}", slotId, definition.Id, catalog.Shifts.Path);

        Relayout();
        ReportPlacement();
    }

    private void ReportPlacement()
    {
        var catalog = PartsCatalog;
        if (_placing is not { Parent: { } parent } part || catalog == null)
        {
            PlacementChanged?.Invoke(null);
            return;
        }

        var (definition, slotId) = PlacedSlot(part, parent);
        var offset = catalog.Shifts.Of(definition.Id, slotId);
        var what = _placementTarget == PlacementTarget.Part
            ? "the part's own slot (every copy of the part)"
            : IsOneOfSeveral(part) ? "the pad it sits on (this one of the set)" : "the pad it sits on";
        PlacementChanged?.Invoke(
            $"PLACEMENT: moving {what}\n{definition.Id} slot {slotId}\n" +
            $"shift so far: x {offset[0] * 100:+0.0;-0.0} cm  y {offset[1] * 100:+0.0;-0.0} cm  z {offset[2] * 100:+0.0;-0.0} cm\n" +
            "arrows: across / fore-aft   PgUp PgDn: height   Ctrl 1 mm  Shift 2 cm\nTab: part / pad   R: reset   F5 or Esc: done");
    }

    /// <summary>Puts every mounted part where the slots say now, without rebuilding the model</summary>
    private void Relayout()
    {
        var renderer = Renderer;
        var catalog = PartsCatalog;
        var anchors = _anchors;
        if (renderer == null || catalog == null || anchors == null || !renderer.HasProp) return;

        var placed = CarPartsLayout.Build(catalog, anchors, Engine, Gear);
        var worlds = placed.Where(p => p.Source != null).ToDictionary(p => p.Source!, p => p.World);
        var moves = new List<(int Node, SlimDX.Matrix World)>();
        for (var i = 0; i < _partNodes.Count; i++)
        {
            if (_partNodes[i].Source is { } source && worlds.TryGetValue(source, out var m))
            {
                moves.Add((i, new SlimDX.Matrix
                {
                    M11 = m.M11, M12 = m.M12, M13 = m.M13, M14 = m.M14,
                    M21 = m.M21, M22 = m.M22, M23 = m.M23, M24 = m.M24,
                    M31 = m.M31, M32 = m.M32, M33 = m.M33, M34 = m.M34,
                    M41 = m.M41, M42 = m.M42, M43 = m.M43, M44 = m.M44
                }));
            }
        }

        renderer.PlaceParts(moves);
        _partWorlds = worlds;
        AnimateFor(AnimationWindow);

        // The places a loose part could go were worked out from where its parent was
        if (_shownCandidates is { Count: > 0 }) ApplyCandidates();
    }

    #endregion

    /// <summary>
    /// Shows the loose part of <see cref="Candidates"/> in the places it could go. Runs from property callbacks, so
    /// nothing may escape it.
    /// </summary>
    private async void ApplyCandidates()
    {
        try
        {
            await ApplyCandidatesAsync();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not show where the part goes");
        }
    }

    private async Task ApplyCandidatesAsync()
    {
        var renderer = Renderer;
        if (renderer == null) return;

        // The places of the part picked before are no places for this one: they go at once, not when the
        // new ones are built, or a click in between would mount this part where the other one fits
        var candidates = Candidates;
        if (!ReferenceEquals(candidates, _shownCandidates)) ClearCandidates(renderer);
        if (_isLoadingCandidates) return;

        var catalog = PartsCatalog;
        var anchors = _anchors;
        if (candidates == null || candidates.Count == 0 || catalog == null || anchors == null || !renderer.HasProp)
        {
            ClearCandidates(renderer);
            return;
        }

        _isLoadingCandidates = true;
        try
        {
            var worlds = _partWorlds;
            var (model, owners) = await Task.Run<(AssemblyModel? Model, MountCandidate[] Owners)>(() =>
            {
                var placed = new List<PlacedPart>();
                var ownerOf = new Dictionary<InstalledPart, MountCandidate>();
                foreach (var candidate in candidates)
                {
                    // A loose rim is a car-level candidate, and car-level candidates are placed in the engine
                    // bay - so a rim on the shelf used to glow green in there. Rims and tyres are not drawn at
                    // all now (see CarPartsLayout.Build), so their ghosts go too. A loose tyre drops out on its
                    // own: its parent is the rim, which no longer has a world to hang off.
                    if (candidate.Parent == null && Parts.Cars.RunningGear.IsWheelSlot(candidate.ParentSlot)) continue;

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

            if (renderer == Renderer && ReferenceEquals(candidates, Candidates))
            {
                renderer.SetCandidates(model?.Kn5);
                _candidateNodes = owners;
                _shownCandidates = candidates;
                AnimateFor(AnimationWindow);
            }
        }
        catch (InvalidOperationException)
        {
            // Nothing made it into the model: nothing to show, nothing to click
            ClearCandidates(renderer);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not show where the part goes");
        }
        finally
        {
            _isLoadingCandidates = false;
        }

        // Likewise a renderer rebuilt while the candidates were put together
        if (Renderer != null && (renderer != Renderer || !ReferenceEquals(candidates, Candidates))) ApplyCandidates();
    }

    private void ClearCandidates(GarageRenderer renderer)
    {
        renderer.SetCandidates(null);
        _candidateNodes = Array.Empty<MountCandidate>();
        _shownCandidates = null;
        Invalidate();
    }

    private void ApplySelection()
    {
        if (Renderer == null) return;

        // The model on screen may still be the one of the tree before: the same part is known by its id there
        var selected = SelectedPart;
        var node = -1;
        for (var i = 0; selected != null && i < _partNodes.Count; i++)
        {
            var source = _partNodes[i].Source;
            if (ReferenceEquals(source, selected) || (source != null && selected.InstanceId != Guid.Empty && source.InstanceId == selected.InstanceId)) node = i;
        }

        Renderer.SelectedPart = node < 0 ? null : node;
        InvalidateIfDirty();
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
        if (Renderer == null || string.IsNullOrEmpty(_loadedCarDirectory)) return;

        var skinId = SkinId;
        if (string.IsNullOrEmpty(skinId)) return;

        if (Directory.Exists(Path.Combine(_loadedCarDirectory, "skins", skinId)))
        {
            Renderer.SelectSkin(skinId);
            Invalidate();
        }
    }

    private void ApplyCarState()
    {
        var renderer = Renderer;
        if (renderer == null) return;

        renderer.AutoRotate = AutoRotate;

        var carNode = renderer.CarNode;
        if (carNode != null)
        {
            carNode.HeadlightsEnabled = HeadlightsOn;
            if (carNode.HasLeftDoorAnimation) carNode.LeftDoorOpen = DoorsOpen;
            if (carNode.HasRightDoorAnimation) carNode.RightDoorOpen = DoorsOpen;
        }

        AnimateFor(AnimationWindow);
    }

    #endregion

    #region Body rock

    protected override bool LeansParts => true;

    /// <summary>The engine moved the body: the car leans, and the parts in it with it</summary>
    protected override void OnPoseChanged() => RockCar(Renderer?.CarNode);

    #endregion

    #region Rendering

    protected override bool StepFrame(GarageRenderer renderer, float dt)
    {
        // The renderer turns the car by itself a little further every tick: it has to keep drawing for that
        var cameraMoving = StepCamera(dt);
        return cameraMoving || renderer.AutoRotate;
    }

    protected override void OnFirstFrame()
    {
        if (FadeInOnReady) FadeIn();
        else ShowPicture();
    }

    protected override void OnRendererDisposed()
    {
        _loadedCarDirectory = null;
        HasCar = false;
        HasDoors = false;
    }

    #endregion

    #region Camera input

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Renderer == null) return;

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

        var orbit = Renderer?.CameraOrbit;
        if (orbit == null) return;

        var position = e.GetPosition(this);
        if (!_isDragging)
        {
            UpdateHover(position);
            return;
        }

        orbit.Alpha += (float)(position.X - _lastMouse.X) * 0.01f;
        orbit.Beta = Math.Clamp(orbit.Beta + (float)(position.Y - _lastMouse.Y) * 0.01f, MinBeta, MaxBeta);
        _alphaGoal = orbit.Alpha;
        _betaGoal = orbit.Beta;
        _lastMouse = position;

        if ((position - _pressedAt).Length > ClickSlack) ShowPartLabel(null, default);
        Invalidate();
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (Renderer != null) Renderer.HoveredPart = null;
        InvalidateIfDirty();
        ShowPartLabel(null, default);
    }

    private PartHit? PickAt(System.Windows.Point position) =>
        Renderer == null || ActualWidth <= 0 || ActualHeight <= 0
            ? null
            : Renderer.Pick((float)(position.X / ActualWidth), (float)(position.Y / ActualHeight));

    private void UpdateHover(System.Windows.Point position)
    {
        if (Renderer is not { HasProp: true } || !PartsVisible) return;

        // Picking tests triangles on the CPU; the pointer moves more often than it needs to be asked
        var now = DateTime.Now;
        if (now - _lastHoverPick < HoverInterval) return;
        _lastHoverPick = now;

        var hit = PickAt(position);
        Renderer.HoveredPart = hit;
        InvalidateIfDirty();
        ShowPartLabel(hit == null ? null : DefinitionOf(hit.Value), position);
    }

    private void OnClick(System.Windows.Point position)
    {
        if (Renderer is not { HasProp: true } || !PartsVisible) return;

        var hit = PickAt(position);
        if (hit is { Layer: PartLayer.Candidate } candidate && candidate.Node < _candidateNodes.Count)
        {
            if (ReferenceEquals(_shownCandidates, Candidates)) CandidateClicked?.Invoke(_candidateNodes[candidate.Node]);
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

    private void ShowPartLabel(PartDefinition? part, System.Windows.Point position) =>
        ShowLabel(part == null ? null : part.DisplayName ?? part.Name, position);

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        var orbit = Renderer?.CameraOrbit;
        if (orbit == null) return;

        var minRadius = Renderer!.HasProp ? PartsMinRadius : MinRadius;
        orbit.Radius = Math.Clamp(orbit.Radius - e.Delta * 0.002f, minRadius, MaxRadius);
        _radiusGoal = orbit.Radius;
        Invalidate();
        e.Handled = true;
    }

    #endregion
}
