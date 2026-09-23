using AcTools.Kn5File;
using AcTools.Render.Base;
using AcTools.Render.Base.Cameras;
using AcTools.Render.Base.Objects;
using AcTools.Render.Base.PostEffects;
using AcTools.Render.Base.TargetTextures;
using AcTools.Render.Base.Utils;
using AcTools.Render.Kn5Specific.Objects;
using AcTools.Render.Kn5SpecificForwardDark;
using AcTools.Render.Shaders;
using SlimDX;
using SlimDX.Direct3D11;
using SlimDX.DXGI;
using Street_Rod_AC.Parts;

namespace Street_Rod_AC.Controls;

/// <summary>Which of the two part models something belongs to</summary>
public enum PartLayer
{
    /// <summary>Parts that are on the car</summary>
    Mounted,

    /// <summary>A loose part shown in the places it could go</summary>
    Candidate
}

/// <summary>A part node of one of the part models, by its index (<see cref="PartAssembler.NodeName"/>)</summary>
public readonly record struct PartHit(PartLayer Layer, int Node);

/// <summary>
/// Dark renderer with one extra model (the car's parts) in the scene, and an x-ray mode for the car.
/// The base renderer only knows about the showroom and car slots, so the prop rides along with the cars:
/// every pass that draws them (shadows, g-buffer, opaque, transparent) draws the prop too.
/// </summary>
public class GarageRenderer : DarkKn5ObjectRenderer
{
    // Dissolving the car also reveals the parts: at full opacity the solid car simply covers them
    private const float GhostTransitionSeconds = 0.8f;
    private const float MaxTickSeconds = 0.05f;

    // A part on its way off the car or onto it: it travels this far from its place, and is small at the far end
    private const float PartMotionSeconds = 0.55f;
    private const float PartTravel = 0.45f;
    private const float PartFarScale = 0.35f;

    private readonly record struct Glow(float Red, float Green, float Blue, float Strength = 1f);

    private static readonly Glow CarGlow = new(1f, 1f, 1f);
    private static readonly Glow CandidateGlow = new(0.35f, 1f, 0.45f);
    private static readonly Glow SelectedGlow = new(1f, 0.62f, 0.15f);
    private static readonly Glow HoverGlow = new(1f, 0.9f, 0.55f);

    private Kn5RenderableFile? _prop;
    private Kn5RenderableFile? _candidates;
    private List<PartNode> _propNodes = new();
    private List<PartNode> _candidateNodes = new();
    private PartHit? _hovered;
    private int? _selected;
    private readonly List<PartMotion> _motions = new();
    private bool _ghostCar;
    private bool _removePropWhenSolid;
    private float _ghostProgress;
    private float _ghostOpacity = 0.3f;

    // Scene copy with the solid car drawn over it, blended back at GhostOpacity
    private TargetResourceTexture? _ghostBuffer;
    private TargetResourceDepthTexture? _ghostDepth;
    private BlendState? _ghostBlend;
    private BlendState? _scaleBlend;

    // Glows: around the car's silhouette, so the shell reads even where the paint is as dark as the garage,
    // around the places a loose part could go, the selected part and the part the pointer is on.
    // One buffer each, all made from the same depth buffer in turn.
    private readonly TargetResourceTexture?[] _glowBuffers = new TargetResourceTexture?[4];
    private readonly bool[] _glowDrawn = new bool[4];
    private TargetResourceDepthTexture? _outlineDepth;

    public GarageRenderer(CarDescription? car, string? showroomKn5) : base(car, showroomKn5) { }

    /// <summary>
    /// Marks the shadow map as needing redrawing.
    ///
    /// The renderer works this out for itself when the car in its main slot changes, but it only listens to
    /// that one slot. On a lot every other car is in a slot it is not listening to, so the last car to
    /// arrive stands there without a shadow until something else happens to ask for one.
    /// </summary>
    public void RefreshShadows() => SetShadowsDirty();

    public bool HasProp => _prop != null;

    /// <summary>
    /// Draws the car as a faint shell over whatever is inside it. Materials force their own blend state,
    /// so instead of fading them the solid car goes to a separate buffer that is blended over the scene;
    /// as a bonus only the nearest surface shows, not every layer of the interior.
    /// </summary>
    public bool GhostCar
    {
        get => _ghostCar;
        set
        {
            if (_ghostCar == value) return;
            _ghostCar = value;
            if (value) _removePropWhenSolid = false;
            IsDirty = true;
        }
    }

    /// <summary>True while the car is anything but solid, transitions included</summary>
    private bool IsGhosting => _ghostProgress > 0f;

    /// <summary>Opacity of the shell right now: from solid down to GhostOpacity as the transition runs</summary>
    private float CurrentGhostOpacity => 1f + (_ghostOpacity - 1f) * Ease(_ghostProgress);

    private static float Ease(float t) => t * t * (3f - 2f * t);

    public float GhostOpacity
    {
        get => _ghostOpacity;
        set
        {
            _ghostOpacity = Math.Clamp(value, 0f, 1f);
            IsDirty = true;
        }
    }

    /// <summary>Brings the car back to solid, then drops the prop it was covering up again</summary>
    public void HideParts()
    {
        if (!IsGhosting)
        {
            GhostCar = false;
            SetProp(null, Matrix.Identity);
            return;
        }

        GhostCar = false;
        _removePropWhenSolid = true;
    }

    /// <summary>True while parts are on their way off the car or onto it</summary>
    public bool IsMovingParts => _motions.Count > 0;

    /// <summary>
    /// Sets mounted parts in motion, given by node: arriving parts start away from the engine and settle into their
    /// place, leaving parts lift off and are gone at the end (until the model is replaced by one without them).
    /// </summary>
    public void MoveParts(IEnumerable<int> nodes, bool arriving)
    {
        if (_prop == null) return;

        _prop.UpdateBoundingBox();
        var moving = nodes.Where(n => n >= 0 && n < _propNodes.Count).Select(n => _propNodes[n]).ToList();
        if (moving.Count == 0) return;

        // Everything that moves together goes the same way: away from the middle of what stays
        var staying = _propNodes.Except(moving).Select(n => n.List.BoundingBox).Where(b => b.HasValue).Select(b => b!.Value.GetCenter()).ToList();
        var centres = moving.Select(n => n.List.BoundingBox).Where(b => b.HasValue).Select(b => b!.Value.GetCenter()).ToList();
        if (centres.Count == 0) return;

        var centre = centres.Aggregate(Vector3.Zero, (sum, c) => sum + c) / centres.Count;
        var direction = Vector3.UnitY;
        if (staying.Count > 0)
        {
            var away = centre - staying.Aggregate(Vector3.Zero, (sum, c) => sum + c) / staying.Count;

            // Mostly up and out, never far down: the floor is right there
            away.Y = Math.Max(away.Y, 0f) + 0.15f;
            if (away.LengthSquared() > 1e-6f) direction = Vector3.Normalize(away);
        }

        foreach (var node in moving)
        {
            _motions.RemoveAll(m => ReferenceEquals(m.Node, node));
            var motion = new PartMotion(node, node.List.LocalMatrix, centre, direction, arriving);
            _motions.Add(motion);
            motion.Apply();
        }

        if (!arriving && _selected is { } selected && selected < _propNodes.Count && moving.Contains(_propNodes[selected])) _selected = null;
        _hovered = null;
        SetShadowsDirty();
        IsDirty = true;
    }

    /// <summary>
    /// Puts mounted parts' nodes where they now belong, at once: the placement mode nudging a part. One pass over
    /// the bounds and shadows for the lot, a nudge moves every part downstream of the slot.
    /// </summary>
    public void PlaceParts(IEnumerable<(int Node, Matrix World)> moves)
    {
        if (_prop == null) return;

        var moved = false;
        foreach (var (node, world) in moves)
        {
            if (node < 0 || node >= _propNodes.Count) continue;

            _motions.RemoveAll(m => ReferenceEquals(m.Node, _propNodes[node]));
            _propNodes[node].List.LocalMatrix = world;
            moved = true;
        }

        if (!moved) return;

        _prop.UpdateBoundingBox();
        SetShadowsDirty();
        IsDirty = true;
    }

    private void TickPartMotions(float dt)
    {
        if (_motions.Count == 0) return;

        var step = Math.Min(dt, MaxTickSeconds) / PartMotionSeconds;
        foreach (var motion in _motions) motion.Advance(step);
        _motions.RemoveAll(m => m.IsDone);

        _prop?.UpdateBoundingBox();
        SetShadowsDirty();
        IsDirty = true;
    }

    /// <summary>One part node travelling between its place and a point away from the engine</summary>
    private sealed class PartMotion
    {
        private readonly Matrix _home;
        private readonly Vector3 _centre;
        private readonly Vector3 _direction;
        private readonly bool _arriving;
        private float _progress;

        public PartMotion(PartNode node, Matrix home, Vector3 centre, Vector3 direction, bool arriving)
        {
            Node = node;
            _home = home;
            _centre = centre;
            _direction = direction;
            _arriving = arriving;
        }

        public PartNode Node { get; }

        public bool IsDone => _progress >= 1f;

        public void Advance(float step)
        {
            _progress = Math.Min(1f, _progress + step);
            Apply();
        }

        public void Apply()
        {
            // 0 = in its place, 1 = away
            var away = _arriving ? 1f - Ease(_progress) : Ease(_progress);
            if (IsDone)
            {
                Node.List.LocalMatrix = _home;
                Node.List.IsEnabled = _arriving;
                return;
            }

            var scale = 1f + (PartFarScale - 1f) * away;
            Node.List.IsEnabled = true;
            Node.List.LocalMatrix = _home
                                    * Matrix.Translation(-_centre)
                                    * Matrix.Scaling(scale, scale, scale)
                                    * Matrix.Translation(_centre + _direction * (PartTravel * away));
        }
    }

    protected override void OnTickOverride(float dt)
    {
        base.OnTickOverride(dt);
        TickPartMotions(dt);

        var target = _ghostCar ? 1f : 0f;
        if (_ghostProgress == target) return;

        // The viewport only draws on demand, so the first tick after a pause can span seconds
        var step = Math.Min(dt, MaxTickSeconds) / GhostTransitionSeconds;
        var wasGhosting = IsGhosting;
        _ghostProgress = _ghostCar ? Math.Min(1f, _ghostProgress + step) : Math.Max(0f, _ghostProgress - step);

        // A ghost casts no shadow, or the parts inside would sit in the dark
        if (wasGhosting != IsGhosting) SetShadowsDirty();

        if (!IsGhosting && _removePropWhenSolid)
        {
            _removePropWhenSolid = false;
            SetProp(null, Matrix.Identity);
        }

        IsDirty = true;
    }

    /// <summary>Replaces the prop; pass null to remove it. Must be called on the thread that draws.</summary>
    public void SetProp(IKn5? kn5, Matrix placement)
    {
        _prop?.Dispose();
        _prop = kn5 == null ? null : new Kn5RenderableFile(kn5, placement, asyncTexturesLoading: false);
        _propNodes = PartNode.Of(_prop);
        _motions.Clear();
        _hovered = null;
        _selected = null;
        SetShadowsDirty();
        IsDirty = true;
    }

    /// <summary>Replaces the model of the places a loose part could go; pass null to remove it</summary>
    public void SetCandidates(IKn5? kn5)
    {
        _candidates?.Dispose();
        _candidates = kn5 == null ? null : new Kn5RenderableFile(kn5, Matrix.Identity, asyncTexturesLoading: false);
        _candidateNodes = PartNode.Of(_candidates);
        if (_hovered?.Layer == PartLayer.Candidate) _hovered = null;
        SetShadowsDirty();
        IsDirty = true;
    }

    /// <summary>The part the pointer is on, glowing so it can be told from its neighbours</summary>
    public PartHit? HoveredPart
    {
        get => _hovered;
        set
        {
            if (_hovered == value) return;
            _hovered = value;
            IsDirty = true;
        }
    }

    /// <summary>Node of the mounted part that is selected</summary>
    public int? SelectedPart
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            IsDirty = true;
        }
    }

    /// <summary>
    /// The part under a point of the picture: the nearest place shown for a loose part, else the nearest
    /// mounted part. The places glow through whatever is in front of them, and some sit inside another part
    /// (a fuel rail in its manifold), so they are reached through the mounted parts.
    /// The car itself is never hit: in the parts view it is a ghost one reaches through.
    /// </summary>
    /// <param name="x">0 at the left edge, 1 at the right</param>
    /// <param name="y">0 at the top, 1 at the bottom</param>
    public PartHit? Pick(float x, float y)
    {
        if (!IsGhosting || Camera == null) return null;

        var ray = Camera.GetPickingRay(new Vector2(x * ActualWidth, y * ActualHeight), new Vector2(ActualWidth, ActualHeight));
        return Nearest(PartLayer.Candidate, _candidateNodes) ?? Nearest(PartLayer.Mounted, _propNodes);

        PartHit? Nearest(PartLayer layer, List<PartNode> nodes)
        {
            PartHit? nearest = null;
            var distance = float.MaxValue;
            for (var i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Intersect(ray) is { } hit && hit < distance)
                {
                    distance = hit;
                    nearest = new PartHit(layer, i);
                }
            }

            return nearest;
        }
    }

    /// <summary>The meshes of one part in a part model</summary>
    private sealed class PartNode
    {
        private readonly List<IKn5RenderableObject> _meshes;

        private PartNode(Kn5RenderableFile file, RenderableList list)
        {
            File = file;
            List = list;
            _meshes = list.GetAllChildren().OfType<IKn5RenderableObject>().ToList();
        }

        public Kn5RenderableFile File { get; }

        public RenderableList List { get; }

        /// <summary>The part nodes of the model, this one included</summary>
        public HashSet<RenderableList> Siblings { get; private set; } = new();

        public static List<PartNode> Of(Kn5RenderableFile? file)
        {
            var nodes = new List<PartNode>();
            if (file == null) return nodes;

            // Part nodes are numbered without gaps
            while (file.GetDummyByName(PartAssembler.NodeName(nodes.Count)) is { } list) nodes.Add(new PartNode(file, list));

            var lists = nodes.Select(n => n.List).ToHashSet();
            foreach (var node in nodes) node.Siblings = lists;
            return nodes;
        }

        /// <summary>Distance along the ray to the part's surface; null when the ray misses it</summary>
        public float? Intersect(Ray ray)
        {
            float? nearest = null;
            foreach (var mesh in _meshes)
            {
                // Triangle tests run per vertex on the CPU: only for meshes whose box the ray goes through
                if (mesh.BoundingBox is { } box && !Ray.Intersects(ray, box, out float _)) continue;

                if (mesh.CheckIntersection(ray) is { } hit && (nearest == null || hit < nearest)) nearest = hit;
            }

            return nearest;
        }
    }

    protected override void DrawCars(DeviceContextHolder holder, ICamera camera, SpecialRenderMode mode)
    {
        if (!IsGhosting)
        {
            base.DrawCars(holder, camera, mode);
            _prop?.Draw(holder, camera, mode);
            return;
        }

        // Shadows, g-buffer (AO, reflections) and the rest see the parts only
        _prop?.Draw(holder, camera, mode);
        _candidates?.Draw(holder, camera, mode);

        if (mode == SpecialRenderMode.Simple)
        {
            DrawGhost(holder, camera);
        }
    }

    protected override void DrawPrepare()
    {
        base.DrawPrepare();
        Array.Clear(_glowDrawn);
        if (!IsGhosting || CarNode == null) return;

        var holder = DeviceContextHolder;
        _outlineDepth ??= TargetResourceDepthTexture.Create();
        _outlineDepth.Resize(holder, Width, Height, null);

        // Everything comes in with the transition
        var fade = Ease(_ghostProgress);
        DrawGlow(0, CarGlow, fade, () => base.DrawCars(holder, ActualCamera, SpecialRenderMode.Outline));

        if (_candidates != null)
            DrawGlow(1, CandidateGlow, fade, () => _candidates.Draw(holder, ActualCamera, SpecialRenderMode.Outline));

        if (_selected is { } selected && selected < _propNodes.Count)
            DrawGlow(2, SelectedGlow, fade, () => DrawNode(_propNodes[selected]));

        var hoverIsSelected = _hovered is { Layer: PartLayer.Mounted } && _hovered.Value.Node == _selected;
        if (_hovered is { } hovered && !hoverIsSelected && NodeOf(hovered) is { } hoveredNode)
            DrawGlow(3, HoverGlow, fade, () => DrawNode(hoveredNode));

        // The model hands its materials down as it draws, so a single part is drawn by keeping the others out
        void DrawNode(PartNode node) => node.File.Draw(holder, ActualCamera, SpecialRenderMode.Outline,
            o => o is not RenderableList list || ReferenceEquals(list, node.List) || !node.Siblings.Contains(list));
    }

    private PartNode? NodeOf(PartHit hit)
    {
        var nodes = hit.Layer == PartLayer.Mounted ? _propNodes : _candidateNodes;
        return hit.Node >= 0 && hit.Node < nodes.Count ? nodes[hit.Node] : null;
    }

    /// <summary>The edge of whatever <paramref name="drawDepth"/> draws, tinted, into a glow buffer of its own</summary>
    private void DrawGlow(int index, Glow tint, float strength, Action drawDepth)
    {
        var holder = DeviceContextHolder;
        var context = DeviceContext;

        var buffer = _glowBuffers[index] ??= TargetResourceTexture.Create(Format.R8G8B8A8_UNorm);
        buffer.Resize(holder, Width, Height, null);

        // Depth of the thing alone, then the edge of what it covers
        context.ClearDepthStencilView(_outlineDepth!.DepthView, DepthStencilClearFlags.Depth | DepthStencilClearFlags.Stencil, 1f, 0);
        context.OutputMerger.SetTargets(_outlineDepth.DepthView);

        // The previous frame ends on full-screen passes: depth off, some other viewport
        context.Rasterizer.SetViewports(Viewport);
        context.OutputMerger.DepthStencilState = null;
        context.OutputMerger.BlendState = null;
        context.Rasterizer.State = holder.States.DoubleSidedState;
        drawDepth();

        context.ClearRenderTargetView(buffer.TargetView, new Color4(0f, 0f, 0f, 0f));
        context.OutputMerger.SetTargets(buffer.TargetView);

        var effect = holder.GetEffect<EffectPpOutline>();
        effect.FxDepthMap.SetResource(_outlineDepth.View);
        effect.FxScreenSize.Set(new Vector4(ActualWidth, ActualHeight, 1f / ActualWidth, 1f / ActualHeight));
        holder.PrepareQuad(effect.LayoutPT);
        effect.TechOutline.DrawAllPasses(context, 6);
        context.Rasterizer.State = null;

        // The edge comes out white: scale it to the tint, and its alpha to how far the transition is.
        // This pass writes nothing of its own, so what it samples does not matter.
        _scaleBlend ??= CreateScaleBlend(holder.Device);
        context.OutputMerger.BlendState = _scaleBlend;
        // SlimDX puts alpha first
        context.OutputMerger.BlendFactor = new Color4(tint.Strength * strength, tint.Red, tint.Green, tint.Blue);
        holder.GetHelper<CopyHelper>().Draw(holder, _outlineDepth.View, buffer.TargetView);
        context.OutputMerger.BlendState = null;

        _glowDrawn[index] = true;
    }

    protected override void DrawAfter()
    {
        base.DrawAfter();
        if (!_glowDrawn.Any(d => d)) return;

        var holder = DeviceContextHolder;
        var context = DeviceContext;

        context.OutputMerger.BlendState = holder.States.TransparentBlendState;
        context.OutputMerger.DepthStencilState = holder.States.DisabledDepthState;
        context.Rasterizer.State = null;
        for (var i = 0; i < _glowBuffers.Length; i++)
        {
            if (_glowDrawn[i]) holder.GetHelper<CopyHelper>().Draw(holder, _glowBuffers[i]!.View, InnerBuffer.TargetView);
        }

        context.OutputMerger.BlendState = null;
        context.OutputMerger.SetTargets(DepthStencilView, InnerBuffer.TargetView);
    }

    private void DrawGhost(DeviceContextHolder holder, ICamera camera)
    {
        var context = holder.DeviceContext;
        var scene = InnerBuffer;

        _ghostBuffer ??= TargetResourceTexture.Create(scene.Format);
        _ghostDepth ??= TargetResourceDepthTexture.Create();
        _ghostBuffer.Resize(holder, Width, Height, null);
        _ghostDepth.Resize(holder, Width, Height, null);

        // Scene so far, then the car on top with a depth buffer of its own
        var copy = holder.GetHelper<CopyHelper>();
        copy.Draw(holder, scene.View, _ghostBuffer.TargetView);

        context.ClearDepthStencilView(_ghostDepth.DepthView, DepthStencilClearFlags.Depth | DepthStencilClearFlags.Stencil, 1f, 0);
        context.OutputMerger.SetTargets(_ghostDepth.DepthView, _ghostBuffer.TargetView);

        context.OutputMerger.DepthStencilState = holder.States.LessEqualDepthState;
        base.DrawCars(holder, camera, SpecialRenderMode.Simple);
        context.OutputMerger.DepthStencilState = holder.States.ReadOnlyDepthState;
        base.DrawCars(holder, camera, SpecialRenderMode.SimpleTransparent);

        // Constant-factor blend, so the result does not depend on what the materials wrote to alpha
        _ghostBlend ??= CreateGhostBlend(holder.Device);
        context.OutputMerger.BlendState = _ghostBlend;
        var opacity = CurrentGhostOpacity;
        context.OutputMerger.BlendFactor = new Color4(opacity, opacity, opacity, opacity);
        context.OutputMerger.DepthStencilState = holder.States.DisabledDepthState;
        context.Rasterizer.State = null;
        copy.Draw(holder, _ghostBuffer.View, scene.TargetView);

        // Back to what the scene pass expects
        context.OutputMerger.BlendState = null;
        context.OutputMerger.SetTargets(DepthStencilView, scene.TargetView);
        context.OutputMerger.DepthStencilState = holder.States.LessEqualDepthState;
    }

    private static BlendState CreateGhostBlend(SlimDX.Direct3D11.Device device)
    {
        var description = new BlendStateDescription { AlphaToCoverageEnable = false, IndependentBlendEnable = false };
        description.RenderTargets[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = BlendOption.BlendFactor,
            DestinationBlend = BlendOption.InverseBlendFactor,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = BlendOption.Zero,
            DestinationBlendAlpha = BlendOption.One,
            BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteMaskFlags.All
        };
        return BlendState.FromDescription(device, description);
    }

    /// <summary>Multiplies what is in the target by the blend factor, colour and alpha alike</summary>
    private static BlendState CreateScaleBlend(SlimDX.Direct3D11.Device device)
    {
        var description = new BlendStateDescription { AlphaToCoverageEnable = false, IndependentBlendEnable = false };
        description.RenderTargets[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = BlendOption.Zero,
            DestinationBlend = BlendOption.BlendFactor,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = BlendOption.Zero,
            DestinationBlendAlpha = BlendOption.BlendFactor,
            BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteMaskFlags.All
        };
        return BlendState.FromDescription(device, description);
    }

    protected override void DisposeOverride()
    {
        _prop?.Dispose();
        _prop = null;
        _candidates?.Dispose();
        _candidates = null;
        _ghostBuffer?.Dispose();
        _ghostDepth?.Dispose();
        _ghostBlend?.Dispose();
        _scaleBlend?.Dispose();
        foreach (var buffer in _glowBuffers) buffer?.Dispose();
        _outlineDepth?.Dispose();
        base.DisposeOverride();
    }
}
