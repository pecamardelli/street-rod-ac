using System;
using System.Windows;
using System.Windows.Threading;
using AcTools.Render.Forward;
using AcTools.Render.Kn5Specific.Objects;
using AcTools.Render.Kn5SpecificForward;
using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Dialogs.Information;
using Street_Rod_AC.Models.AC;
using WinForms = System.Windows.Forms;
using WinFormsIntegration = System.Windows.Forms.Integration;

namespace Street_Rod_AC;

public partial class CarRendererWindow : Window
{
    private readonly DialogService _dialogService;
    private ForwardKn5ObjectRenderer? _renderer;
    private WinForms.Panel? _renderPanel;
    private DispatcherTimer? _renderTimer;
    private System.Drawing.Point _lastMousePosition;
    private bool _isMouseDown;
    private DateTime _lastFrameTime = DateTime.Now;
    private string _baseTitle = "";
    private WindowStyle _previousWindowStyle;
    private WindowState _previousWindowState;
    private bool _isFullscreen = false;

    public CarRendererWindow(CarInfo carInfo)
    {
        InitializeComponent();

        var app = (App)System.Windows.Application.Current;
        _dialogService = app.DialogService;

        _baseTitle = $"Car Viewer - {carInfo.Brand} {carInfo.Name}";
        Title = _baseTitle;

        try
        {
            // Create car description
            var carDesc = CarDescription.FromDirectory(carInfo.FolderPath);

            // Create renderer
            _renderer = new ForwardKn5ObjectRenderer(carDesc);
            _renderer.Width = 1920;
            _renderer.Height = 1080;
            _renderer.AutoRotate = false;

            // Minimal settings - will add quality settings after initialization
        }
        catch (Exception ex)
        {
            var errorDialog = new InformationDialogViewModel(
                _dialogService,
                $"Failed to create renderer:\n\n{ex.Message}",
                "Renderer Error");
            _dialogService.ShowDialog(errorDialog);
            Close();
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_renderer == null) return;

        try
        {
            // Create Windows Forms panel for rendering
            _renderPanel = new WinForms.Panel
            {
                Dock = WinForms.DockStyle.Fill
            };

            // Setup mouse events for camera control
            _renderPanel.MouseDown += RenderPanel_MouseDown;
            _renderPanel.MouseMove += RenderPanel_MouseMove;
            _renderPanel.MouseUp += RenderPanel_MouseUp;
            _renderPanel.MouseWheel += RenderPanel_MouseWheel;

            // Add panel to WPF host
            RenderHost.Child = _renderPanel;

            // Initialize renderer with panel handle
            _renderer.Initialize(_renderPanel.Handle);

            // ========== ALL QUALITY SETTINGS (AFTER INITIALIZATION) ==========

            // Resolution & Super-Sampling
            _renderer.ResolutionMultiplier = 2.0;  // Render at 4K (3840x2160) internally
            _renderer.UseSsaa = true;  // Enable super-sampling anti-aliasing

            // Anti-Aliasing (Quad-stack for ultimate smoothness)
            _renderer.UseMsaa = true;
            _renderer.MsaaSampleCount = 8;  // Maximum MSAA (8x samples per pixel)
            _renderer.UseFxaa = true;  // Fast approximate AA
            _renderer.UseSmaa = true;  // Subpixel morphological AA

            // Shadows - Maximum Quality
            _renderer.EnableShadows = true;  // Enable shadow rendering
            _renderer.UsePcss = true;  // Percentage-Closer Soft Shadows (best quality)
            _renderer.CarShadowsOpacity = 1.0f;  // Full shadow opacity

            // Reflections - Ultra Resolution
            _renderer.CubemapReflectionMapSize = 4096;  // 4K cubemap resolution
            _renderer.CubemapReflectionFacesPerFrame = 6;  // Update all 6 faces every frame
            _renderer.ForceUpdateWholeCubemapAtOnce = true;  // No frame delays
            _renderer.ReflectionCubemapAtCamera = true;  // Position at camera for best reflections

            // Post-Processing Effects
            _renderer.UseBloom = true;  // HDR bloom
            _renderer.BloomRadiusMultiplier = 1.5f;  // Enhanced bloom radius
            _renderer.UseLensFlares = true;  // Lens flare effects
            _renderer.UseDither = true;  // Dithering to prevent color banding

            // Tone Mapping - Filmic look
            _renderer.ToneMapping = ToneMappingFn.Uncharted2;  // Best cinematic tone mapping
            _renderer.ToneExposure = 1.0f;  // Optimal exposure
            _renderer.ToneGamma = 2.2f;  // Standard gamma correction
            _renderer.ToneWhitePoint = 2.0f;  // Higher white point for better highlights

            // V-Sync OFF for unlimited FPS
            _renderer.SyncInterval = false;  // Disable V-Sync to see true performance

            // Setup camera - 3/4 view from front corner
            if (_renderer.CameraOrbit != null)
            {
                _renderer.CameraOrbit.Radius = 7.5f;  // Farther distance
                _renderer.CameraOrbit.Alpha = 0.8f;  // 45° horizontal angle (π/4 radians) for 3/4 view
                _renderer.CameraOrbit.Beta = 0.2f;  // Slight downward angle
            }

            // Setup render loop
            _renderTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1) // Maximum FPS (let renderer control frame rate)
            };
            _renderTimer.Tick += RenderTimer_Tick;
            _renderTimer.Start();
        }
        catch (Exception ex)
        {
            var errorDialog = new InformationDialogViewModel(
                _dialogService,
                $"Failed to initialize renderer:\n\n{ex.Message}",
                "Initialization Error");
            _dialogService.ShowDialog(errorDialog);
            Close();
        }
    }

    private void RenderTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            // Calculate and display FPS
            var now = DateTime.Now;
            var fps = 1000.0 / (now - _lastFrameTime).TotalMilliseconds;
            Title = $"{_baseTitle} - FPS: {fps:F1}";
            _lastFrameTime = now;

            _renderer?.Draw();
        }
        catch (Exception ex)
        {
            _renderTimer?.Stop();
            var errorDialog = new InformationDialogViewModel(
                _dialogService,
                $"Rendering error:\n\n{ex.Message}",
                "Render Error");
            _dialogService.ShowDialog(errorDialog);
        }
    }

    private void RenderPanel_MouseDown(object? sender, WinForms.MouseEventArgs e)
    {
        if (e.Button == WinForms.MouseButtons.Left)
        {
            _isMouseDown = true;
            _lastMousePosition = e.Location;
        }
    }

    private void RenderPanel_MouseMove(object? sender, WinForms.MouseEventArgs e)
    {
        if (_isMouseDown && _renderer?.CameraOrbit != null)
        {
            var dx = e.X - _lastMousePosition.X;
            var dy = e.Y - _lastMousePosition.Y;

            _renderer.CameraOrbit.Alpha += dx * 0.01f;
            _renderer.CameraOrbit.Beta += dy * 0.01f;

            // Clamp beta to prevent flipping
            _renderer.CameraOrbit.Beta = Math.Clamp(_renderer.CameraOrbit.Beta, -1.5f, 1.5f);

            _lastMousePosition = e.Location;
            _renderer.IsDirty = true;
        }
    }

    private void RenderPanel_MouseUp(object? sender, WinForms.MouseEventArgs e)
    {
        if (e.Button == WinForms.MouseButtons.Left)
        {
            _isMouseDown = false;
        }
    }

    private void RenderPanel_MouseWheel(object? sender, WinForms.MouseEventArgs e)
    {
        if (_renderer?.CameraOrbit != null)
        {
            _renderer.CameraOrbit.Radius -= e.Delta * 0.001f;
            _renderer.CameraOrbit.Radius = Math.Clamp(_renderer.CameraOrbit.Radius, 2.0f, 20.0f);

            // Update slider
            ZoomSlider.Value = _renderer.CameraOrbit.Radius;

            _renderer.IsDirty = true;
        }
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_renderer?.CameraOrbit != null)
        {
            _renderer.CameraOrbit.Radius = (float)e.NewValue;
            _renderer.IsDirty = true;
        }
    }

    private void AutoRotateCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_renderer != null)
        {
            _renderer.AutoRotate = AutoRotateCheck.IsChecked ?? false;
        }
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_renderer != null && _renderPanel != null)
        {
            // Update renderer size to match window
            var renderSize = RenderHost.RenderSize;
            _renderer.Width = (int)renderSize.Width;
            _renderer.Height = (int)renderSize.Height;
        }
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // F11 to toggle fullscreen
        if (e.Key == System.Windows.Input.Key.F11)
        {
            ToggleFullscreen();
        }
    }

    private void ToggleFullscreen()
    {
        if (_isFullscreen)
        {
            // Exit fullscreen
            WindowStyle = _previousWindowStyle;
            WindowState = _previousWindowState;
            _isFullscreen = false;
        }
        else
        {
            // Enter fullscreen
            _previousWindowStyle = WindowStyle;
            _previousWindowState = WindowState;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            _isFullscreen = true;
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Stop render loop
        _renderTimer?.Stop();

        // Dispose renderer
        _renderer?.Dispose();

        // Dispose panel
        _renderPanel?.Dispose();
    }
}
