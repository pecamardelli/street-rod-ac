using AcTools.Kn5File;
using AcTools.Render.Base;
using AcTools.Render.Base.Cameras;
using AcTools.Render.Base.Objects;
using AcTools.Render.Base.PostEffects;
using AcTools.Render.Base.TargetTextures;
using AcTools.Render.Kn5Specific.Objects;
using AcTools.Render.Kn5SpecificForwardDark;
using AcTools.Render.Shaders;
using SlimDX;
using SlimDX.Direct3D11;
using SlimDX.DXGI;

namespace Street_Rod_AC.Controls;

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

    private Kn5RenderableFile? _prop;
    private bool _ghostCar;
    private bool _removePropWhenSolid;
    private float _ghostProgress;
    private float _ghostOpacity = 0.3f;

    // Scene copy with the solid car drawn over it, blended back at GhostOpacity
    private TargetResourceTexture? _ghostBuffer;
    private TargetResourceDepthTexture? _ghostDepth;
    private BlendState? _ghostBlend;
    private BlendState? _alphaScaleBlend;

    // Glow around the car's silhouette, so the shell reads even where the paint is as dark as the garage
    private TargetResourceTexture? _outlineBuffer;
    private TargetResourceDepthTexture? _outlineDepth;

    public GarageRenderer(CarDescription? car, string? showroomKn5) : base(car, showroomKn5) { }

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

    protected override void OnTickOverride(float dt)
    {
        base.OnTickOverride(dt);

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
        SetShadowsDirty();
        IsDirty = true;
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

        if (mode == SpecialRenderMode.Simple)
        {
            DrawGhost(holder, camera);
        }
    }

    protected override void DrawPrepare()
    {
        base.DrawPrepare();
        if (!IsGhosting || CarNode == null) return;

        var holder = DeviceContextHolder;
        var context = DeviceContext;

        _outlineBuffer ??= TargetResourceTexture.Create(Format.R8G8B8A8_UNorm);
        _outlineDepth ??= TargetResourceDepthTexture.Create();
        _outlineBuffer.Resize(holder, Width, Height, null);
        _outlineDepth.Resize(holder, Width, Height, null);

        // Car's depth alone, then the edge of what it covers
        context.ClearDepthStencilView(_outlineDepth.DepthView, DepthStencilClearFlags.Depth | DepthStencilClearFlags.Stencil, 1f, 0);
        context.OutputMerger.SetTargets(_outlineDepth.DepthView);

        // The previous frame ends on full-screen passes: depth off, some other viewport
        context.Rasterizer.SetViewports(Viewport);
        context.OutputMerger.DepthStencilState = null;
        context.OutputMerger.BlendState = null;
        context.Rasterizer.State = holder.States.DoubleSidedState;
        base.DrawCars(holder, ActualCamera, SpecialRenderMode.Outline);

        context.ClearRenderTargetView(_outlineBuffer.TargetView, new Color4(0f, 0f, 0f, 0f));
        context.OutputMerger.SetTargets(_outlineBuffer.TargetView);

        var effect = holder.GetEffect<EffectPpOutline>();
        effect.FxDepthMap.SetResource(_outlineDepth.View);
        effect.FxScreenSize.Set(new Vector4(ActualWidth, ActualHeight, 1f / ActualWidth, 1f / ActualHeight));
        holder.PrepareQuad(effect.LayoutPT);
        effect.TechOutline.DrawAllPasses(context, 6);
        context.Rasterizer.State = null;

        // The glow comes in with the transition: scale the alpha it was written with.
        // This pass writes nothing of its own, so what it samples does not matter.
        var glow = Ease(_ghostProgress);
        if (glow < 1f)
        {
            _alphaScaleBlend ??= CreateAlphaScaleBlend(holder.Device);
            context.OutputMerger.BlendState = _alphaScaleBlend;
            context.OutputMerger.BlendFactor = new Color4(glow, glow, glow, glow);
            holder.GetHelper<CopyHelper>().Draw(holder, _outlineDepth.View, _outlineBuffer.TargetView);
            context.OutputMerger.BlendState = null;
        }
    }

    protected override void DrawAfter()
    {
        base.DrawAfter();
        if (!IsGhosting || _outlineBuffer == null || CarNode == null) return;

        var holder = DeviceContextHolder;
        var context = DeviceContext;

        context.OutputMerger.BlendState = holder.States.TransparentBlendState;
        context.OutputMerger.DepthStencilState = holder.States.DisabledDepthState;
        context.Rasterizer.State = null;
        holder.GetHelper<CopyHelper>().Draw(holder, _outlineBuffer.View, InnerBuffer.TargetView);

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

    /// <summary>Leaves colour alone and multiplies the target's alpha by the blend factor</summary>
    private static BlendState CreateAlphaScaleBlend(SlimDX.Direct3D11.Device device)
    {
        var description = new BlendStateDescription { AlphaToCoverageEnable = false, IndependentBlendEnable = false };
        description.RenderTargets[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = BlendOption.Zero,
            DestinationBlend = BlendOption.One,
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
        _ghostBuffer?.Dispose();
        _ghostDepth?.Dispose();
        _ghostBlend?.Dispose();
        _alphaScaleBlend?.Dispose();
        _outlineBuffer?.Dispose();
        _outlineDepth?.Dispose();
        base.DisposeOverride();
    }
}
