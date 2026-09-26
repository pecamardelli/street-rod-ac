using System.IO;
using System.Windows;
using System.Windows.Input;
using AcTools.Render.Kn5Specific.Objects;
using AcTools.Render.Kn5SpecificForwardDark.Lights;
using Street_Rod_AC.Audio;
using Street_Rod_AC.Controls.Street;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Street;
using CarSlot = AcTools.Render.Kn5SpecificForward.ForwardKn5ObjectRenderer.CarSlot;
using Sx = SlimDX;

namespace Street_Rod_AC.Controls;

/// <summary>
/// The Cruise screen's street, seen from the driver's seat of the player's parked car, looking out of the side window
/// at the lane beside it. A rival drives up that lane and stops alongside, or pulls away, as the screen's
/// <see cref="StreetStage"/> says: its wheels turn, its body dives on the brakes and squats as it leaves, its brake
/// lights come on, and its engine is heard from where the car is. The player's own engine rocks the car they sit in,
/// and the view with it. Dragging with the mouse looks round.
/// </summary>
public class StreetViewport3D : D3DViewportBase
{
    // Where the driver looks: degrees from straight ahead towards the lane, and how far they can turn
    private const float DefaultLookYaw = 72f;
    private const float MinLookYaw = -35f;
    private const float MaxLookYaw = 160f;
    private const float MaxLookPitch = 20f;
    private const float LookDegreesPerPixel = 0.18f;
    private const float FieldOfView = 56f;

    // Where the eyes are in a car that does not say: a little left of the middle, over the seat
    private static readonly Sx.Vector3 FallbackEye = new(0.36f, 1.05f, -0.15f);

    // Loud beside the window, fading as it goes up the street; the player's own engine heard from inside
    private const float RivalFullVolumeWithin = 5f;
    private const float RivalQuietest = 0.06f;
    private const float OwnEngineVolume = 0.55f;

    private const int SettleFrames = 4;
    private static readonly TimeSpan SceneFadeOut = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan SceneFadeIn = TimeSpan.FromMilliseconds(900);

    // What stands in the scene
    private string? _kn5;
    private StreetCar? _player;
    private StreetScene? _scene;
    private StreetStage? _stageListened;

    // The player's eyes in their car's own space
    private Sx.Vector3 _eye = FallbackEye;
    private float _lookYaw = DefaultLookYaw;
    private float _lookPitch = -2f;
    private System.Windows.Point? _dragFrom;

    // The rival's car
    private CarSlot? _rivalSlot;
    private StreetCar? _rivalShown;
    private int _rivalLoad;
    private bool _rivalReady;
    private RivalDrive? _drive;
    private float _driveTime;
    private float _rivalPosition;
    private float _wheelAngle;
    private readonly ChassisPitch _pitch = new();
    private CarBodyRock? _rivalRock;

    private int _fadeInAfter = -1;

    public StreetViewport3D()
        : base("Street", new LabelLook(13, 0xD8, 0x90, new Thickness(8, 4, 8, 4), new Vector(16, 16)))
    {
        Cursor = System.Windows.Input.Cursors.Hand;
    }

    protected override string ViewportName => "Street viewport";

    #region Dependency properties

    public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(
        nameof(Scene), typeof(StreetScene), typeof(StreetViewport3D),
        new PropertyMetadata(null, (d, _) => ((StreetViewport3D)d).RequestLoad()));

    /// <summary>The street</summary>
    public StreetScene? Scene
    {
        get => (StreetScene?)GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public static readonly DependencyProperty LightProperty = DependencyProperty.Register(
        nameof(Light), typeof(StreetLight), typeof(StreetViewport3D),
        new PropertyMetadata(StreetLight.Day, (d, _) => ((StreetViewport3D)d).RequestLoad()));

    /// <summary>Day, dusk or night: another model of the street, and another light</summary>
    public StreetLight Light
    {
        get => (StreetLight)GetValue(LightProperty);
        set => SetValue(LightProperty, value);
    }

    public static readonly DependencyProperty PlayerCarProperty = DependencyProperty.Register(
        nameof(PlayerCar), typeof(StreetCar), typeof(StreetViewport3D),
        new PropertyMetadata(null, (d, _) => ((StreetViewport3D)d).RequestLoad()));

    /// <summary>The car the player sits in</summary>
    public StreetCar? PlayerCar
    {
        get => (StreetCar?)GetValue(PlayerCarProperty);
        set => SetValue(PlayerCarProperty, value);
    }

    public static readonly DependencyProperty StageProperty = DependencyProperty.Register(
        nameof(Stage), typeof(StreetStage), typeof(StreetViewport3D),
        new PropertyMetadata(null, (d, _) => ((StreetViewport3D)d).OnStageChanged()));

    /// <summary>What the rival does, as the screen decides</summary>
    public StreetStage? Stage
    {
        get => (StreetStage?)GetValue(StageProperty);
        set => SetValue(StageProperty, value);
    }

    #endregion

    #region Loading

    private string? WantedKn5 => Scene?.Kn5For(Light);

    protected override bool IsUpToDate => Scene == null || PlayerCar == null
        ? Renderer == null
        : Renderer != null && _kn5 == WantedKn5 && _player == PlayerCar;

    protected override string LoadDescription => $"{PlayerCar?.Directory} in {WantedKn5}";

    protected override async Task LoadAsync()
    {
        var scene = Scene;
        var player = PlayerCar;
        var kn5 = WantedKn5;
        if (scene == null || player == null || kn5 == null)
        {
            DisposeRenderer(releaseDevice: false);
            return;
        }

        if (Renderer != null && !IsPictureHidden)
        {
            await FadeOutAsync(SceneFadeOut);
            if (!IsLoaded) return;
        }

        DisposeRenderer(releaseDevice: false);

        var light = Light;
        var renderer = NewRenderer(null, kn5);
        renderer.AutoAdjustTarget = false;

        // Headlights only at night; by day a car's light sources are only more for the shader to go through
        renderer.LoadCarLights = light == StreetLight.Night;
        renderer.TryToGuessCarLights = light == StreetLight.Night;

        if (!await StartRendererAsync(renderer)) return;

        ApplyLighting(renderer, scene, light);

        var skin = player.Skin != null && Directory.Exists(Path.Combine(player.Directory, "skins", player.Skin))
            ? player.Skin
            : Kn5RenderableCar.DefaultSkin;
        var placement = Placement(scene, scene.Player, 0f);
        renderer.MainSlot.LocalMatrix = placement;
        await TrackDeviceWork(renderer.MainSlot.SetCarAsync(CarDescription.FromDirectory(player.Directory), skin));
        if (Renderer != renderer || !IsLoaded) return;

        renderer.MainSlot.LocalMatrix = placement;
        renderer.MainSlot.ResetCarBoundingBox();
        if (renderer.CarNode is { } car && light == StreetLight.Night) car.HeadlightsEnabled = true;

        _kn5 = kn5;
        _player = player;
        _scene = scene;
        _eye = EyeOf(renderer, placement);
        _lookYaw = DefaultLookYaw;
        _lookPitch = -2f;

        renderer.UseFpsCamera = true;
        AimCamera(renderer);
        renderer.RefreshShadows();

        Logger.Information("Street {Street} up at {Light} with {Car}", scene.Id, light, Path.GetFileName(player.Directory));

        if (Stage is { } stage)
        {
            stage.IsShown = true;

            // A rival already there comes back into the new scene where the stage has it
            if (stage.Phase != RivalPhase.None) _ = ShowRivalAsync(renderer, stage);
        }

        OnPoseChanged();
        _fadeInAfter = SettleFrames;
        Invalidate();
    }

    private static void ApplyLighting(GarageRenderer renderer, StreetScene scene, StreetLight light)
    {
        var lighting = scene.LightingFor(light);
        renderer.Light = new Sx.Vector3(scene.Sun[0], scene.Sun[1], scene.Sun[2]);
        renderer.LightBrightness = lighting.Brightness;
        renderer.LightColor = Color(lighting.Color);
        renderer.AmbientBrightness = lighting.Ambient;
        renderer.AmbientUp = Color(lighting.AmbientUp);
        renderer.AmbientDown = Color(lighting.AmbientDown);
        renderer.CubemapAmbient = lighting.CubemapAmbient;

        foreach (var lamp in lighting.Lamps)
        {
            renderer.AddLight(new DarkPointLight
            {
                Position = new Sx.Vector3(lamp.X, lamp.Y, lamp.Z),
                Color = Color(lamp.Color),
                Brightness = lamp.Brightness,
                Range = lamp.Range,
                UseShadows = false,
                IsVisibleInUi = false
            });
        }
    }

    private static System.Drawing.Color Color(byte[] rgb) => System.Drawing.Color.FromArgb(rgb[0], rgb[1], rgb[2]);

    /// <summary>Where the player's eyes are, in their car's own space: the car's driver camera, or about there</summary>
    private Sx.Vector3 EyeOf(GarageRenderer renderer, Sx.Matrix placement)
    {
        try
        {
            if (renderer.CarNode?.GetDriverCamera() is { } camera)
            {
                return Sx.Vector3.TransformCoordinate(camera.Position, Sx.Matrix.Invert(placement));
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "The driver's eyes of {Car} could not be read; guessed", _player?.Directory);
        }

        return FallbackEye;
    }

    /// <summary>A car standing at <paramref name="spot"/>, moved <paramref name="along"/> metres ahead of it</summary>
    private static Sx.Matrix Placement(StreetScene scene, StreetSpot spot, float along, float aside = 0f)
    {
        var heading = spot.Heading * MathF.PI / 180f;
        var forward = new Sx.Vector3(MathF.Sin(heading), 0f, MathF.Cos(heading));

        // A car's own +x is its left (its driver's side, in the cars of the day)
        var left = new Sx.Vector3(MathF.Cos(heading), 0f, -MathF.Sin(heading));
        var at = new Sx.Vector3(spot.X, 0f, spot.Z) + forward * along + left * aside;
        return Sx.Matrix.RotationY(heading) * Sx.Matrix.Translation(at);
    }

    protected override void OnRendererDisposed()
    {
        if (_stageListened != null) _stageListened.IsShown = false;
        _kn5 = null;
        _player = null;
        _scene = null;
        _rivalSlot = null;
        _rivalShown = null;
        _rivalReady = false;
        _rivalRock = null;
        _rivalLoad++;
        _fadeInAfter = -1;

        // Whatever the rival was doing, it has no car to do it in: the stage hears of it as if at once
        if (_stageListened is { Phase: RivalPhase.Arriving }) _stageListened.ReportAlongside();
        if (_stageListened is { Phase: RivalPhase.Leaving }) _stageListened.ReportGone();
        _drive = null;
    }

    #endregion

    #region The rival

    private void OnStageChanged()
    {
        if (_stageListened != null)
        {
            _stageListened.Changed -= OnStageAction;
            _stageListened.IsShown = false;
        }

        _stageListened = Stage;
        if (_stageListened != null)
        {
            _stageListened.Changed += OnStageAction;
            _stageListened.IsShown = Renderer != null && _kn5 != null;
        }
    }

    private void OnStageAction()
    {
        var renderer = Renderer;
        var stage = _stageListened;
        if (renderer == null || stage == null || _scene == null) return;

        switch (stage.Phase)
        {
            case RivalPhase.Arriving:
                _ = ShowRivalAsync(renderer, stage);
                break;
            case RivalPhase.Leaving:
                StartLeaving(stage);
                break;
            case RivalPhase.None:
                HideRival();
                break;
        }
    }

    /// <summary>
    /// Puts the rival's car in its slot, if it is not the one there already, and starts it up the lane. A car that
    /// will not load is not seen: the stage hears it arrived all the same, so the screen goes on.
    /// </summary>
    private async Task ShowRivalAsync(GarageRenderer renderer, StreetStage stage)
    {
        var car = stage.RivalCar;
        var scene = _scene;
        if (car == null || scene == null) return;

        var load = ++_rivalLoad;
        _drive = null;
        _rivalReady = false;

        try
        {
            if (_rivalSlot == null || _rivalShown != car)
            {
                _rivalRock?.Release();
                _rivalRock = null;

                var hidden = Sx.Matrix.Translation(0f, -50f, 0f);
                _rivalSlot ??= renderer.AddCar(null);
                _rivalSlot.LocalMatrix = hidden;

                var skin = car.Skin != null && Directory.Exists(Path.Combine(car.Directory, "skins", car.Skin))
                    ? car.Skin
                    : Kn5RenderableCar.DefaultSkin;
                await TrackDeviceWork(_rivalSlot.SetCarAsync(CarDescription.FromDirectory(car.Directory), skin));
                if (Renderer != renderer || load != _rivalLoad) return;

                _rivalShown = car;
                if (_rivalSlot.CarNode is { } node && Light == StreetLight.Night) node.HeadlightsEnabled = true;
            }
        }
        catch (Exception ex) when (!IsDeviceLost(ex))
        {
            Logger.Error(ex, "The rival's car {Car} could not be shown", car.Directory);
            _rivalShown = null;
            stage.ReportAlongside();
            return;
        }

        if (_rivalSlot?.CarNode == null)
        {
            stage.ReportAlongside();
            return;
        }

        _rivalReady = true;
        _pitch.Reset();

        var engine = stage.RivalEngine;
        var idle = engine?.Spec?.IdleRpm is > 200 and var i ? i : 800;
        var limiter = engine?.Spec?.LimiterRpm is > 1000 and var l ? l : 6000;

        if (stage.Phase == RivalPhase.Alongside)
        {
            // Came back into a new scene: already stopped where it was
            _rivalPosition = scene.RivalStop;
            _drive = null;
        }
        else
        {
            _drive = RivalDrive.Arrive(-scene.ApproachFrom, scene.RivalStop, idle, limiter);
            _driveTime = 0f;
            _rivalPosition = -scene.ApproachFrom;
        }

        engine?.StartRunning();
        PlaceRival();
        AnimateFor(TimeSpan.FromSeconds(1));
    }

    private void StartLeaving(StreetStage stage)
    {
        if (!_rivalReady || _scene == null)
        {
            stage.ReportGone();
            return;
        }

        var engine = stage.RivalEngine;
        var idle = engine?.Spec?.IdleRpm is > 200 and var i ? i : 800;
        var limiter = engine?.Spec?.LimiterRpm is > 1000 and var l ? l : 6000;
        engine?.SetPedal(null);
        _drive = RivalDrive.Leave(_rivalPosition, _scene.LeaveTo, idle, limiter);
        _driveTime = 0f;
        AnimateFor(TimeSpan.FromSeconds(1));
    }

    private void HideRival()
    {
        _drive = null;
        _rivalReady = false;
        _rivalLoad++;
        _rivalRock?.Release();
        _rivalRock = null;
        if (_rivalSlot != null) _rivalSlot.LocalMatrix = Sx.Matrix.Translation(0f, -50f, 0f);
        Invalidate();
    }

    private void PlaceRival()
    {
        if (_rivalSlot?.CarNode is not { } node || _scene is not { } scene) return;

        var placement = Placement(scene, scene.Player, _rivalPosition, scene.LaneOffset);
        var engine = _stageListened?.RivalEngine;
        var pose = engine?.Pose ?? BodyPose.Rest;
        var body = new BodyPose(pose.Roll, pose.Pitch + _pitch.Pitch, pose.Heave);

        _rivalRock ??= new CarBodyRock(node);
        _rivalRock.Apply(body, _wheelAngle, placement);
    }

    /// <summary>Moves the rival along its drive by a frame; true while it has somewhere to be</summary>
    private bool StepRival(float dt)
    {
        var stage = _stageListened;
        if (!_rivalReady || stage == null) return false;

        var engine = stage.RivalEngine;
        if (_drive is { } drive)
        {
            _driveTime += dt;
            var state = drive.At(_driveTime);
            _wheelAngle += RivalDrive.WheelAngle(state.Position - _rivalPosition);
            _rivalPosition = state.Position;
            _pitch.Tick(dt, state.Acceleration);
            engine?.Drive(state.Rpm, state.Throttle);
            if (_rivalSlot?.CarNode is { } node) node.BrakeLightsEnabled = state.Braking;

            if (state.Done)
            {
                _drive = null;
                engine?.Drive(null);
                if (drive.IsLeaving)
                {
                    HideRival();
                    stage.ReportGone();
                    return false;
                }

                stage.ReportAlongside();
            }
        }
        else
        {
            _pitch.Tick(dt, 0);
        }

        PlaceRival();
        HearRival();
        return true;
    }

    /// <summary>The rival's engine heard from where the car is, from where the player looks</summary>
    private void HearRival()
    {
        if (_stageListened?.RivalEngine is not { } engine || _scene is not { } scene || Renderer?.Camera is not { } camera) return;

        var at = Placement(scene, scene.Player, _rivalPosition, scene.LaneOffset);
        var car = new Sx.Vector3(at.M41, 0.5f, at.M43);
        var offset = car - camera.Position;
        var distance = offset.Length();

        // Into the listener's own axes: x right, y up, z where they look
        var look = Sx.Vector3.Normalize(camera.Look);
        var right = Sx.Vector3.Normalize(Sx.Vector3.Cross(Sx.Vector3.UnitY, look));
        var up = Sx.Vector3.Cross(look, right);
        var direction = (Sx.Vector3.Dot(offset, right), Sx.Vector3.Dot(offset, up), Sx.Vector3.Dot(offset, look));

        var volume = Math.Clamp(RivalFullVolumeWithin / Math.Max(distance, 0.1f), RivalQuietest, 1f);
        engine.Place(direction, volume);
    }

    #endregion

    #region Camera

    private void AimCamera(GarageRenderer renderer)
    {
        if (renderer.Camera is not { } camera || _scene is not { } scene) return;

        var placement = Placement(scene, scene.Player, 0f);
        var lean = CarLean;

        // In the car's own space: +z ahead, +x to the left, where the lane is
        var yaw = _lookYaw * MathF.PI / 180f;
        var pitch = _lookPitch * MathF.PI / 180f;
        var look = new Sx.Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch));

        var eye = Sx.Vector3.TransformCoordinate(Sx.Vector3.TransformCoordinate(_eye, placement), lean);
        var direction = Sx.Vector3.TransformNormal(Sx.Vector3.TransformNormal(look, placement), lean);
        var up = Sx.Vector3.TransformNormal(Sx.Vector3.UnitY, lean);

        camera.FovY = FieldOfView * MathF.PI / 180f;
        camera.LookAt(eye, eye + direction, up);
        camera.SetLens(renderer.AspectRatio);
        renderer.IsDirty = true;
    }

    /// <summary>Dragging looks round; a double click looks back out of the window</summary>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2)
        {
            _lookYaw = DefaultLookYaw;
            _lookPitch = -2f;
            if (Renderer is { } renderer) AimCamera(renderer);
            Invalidate();
            e.Handled = true;
            return;
        }

        _dragFrom = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragFrom = null;
        ReleaseMouseCapture();
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragFrom is not { } from || e.LeftButton != MouseButtonState.Pressed) return;

        var now = e.GetPosition(this);
        _lookYaw = Math.Clamp(_lookYaw + (float)(from.X - now.X) * LookDegreesPerPixel, MinLookYaw, MaxLookYaw);
        _lookPitch = Math.Clamp(_lookPitch + (float)(from.Y - now.Y) * LookDegreesPerPixel, -MaxLookPitch, MaxLookPitch);
        _dragFrom = now;

        if (Renderer is { } renderer) AimCamera(renderer);
        Invalidate();
    }

    #endregion

    #region Rendering

    protected override bool StepFrame(GarageRenderer renderer, float dt)
    {
        var moving = StepRival(dt);

        // The player's engine shakes the car, and the view with it
        if (RunningEngine is { IsActive: true } own)
        {
            own.Place(null, OwnEngineVolume);
            moving = true;
        }

        AimCamera(renderer);
        return moving || !_pitch.IsSettled;
    }

    protected override void OnFramePresented()
    {
        if (_fadeInAfter < 0) return;
        if (_fadeInAfter-- > 0) return;

        if (IsPictureHidden) FadeIn(SceneFadeIn);
    }

    protected override void OnPoseChanged()
    {
        RockCar(Renderer?.CarNode);
        if (Renderer is { } renderer) AimCamera(renderer);
    }

    #endregion
}
