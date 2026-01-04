using System;
using System.Windows;
using System.Windows.Threading;
using AcTools.Render.Kn5Specific.Objects;
using AcTools.Render.Kn5SpecificForward;
using Street_Rod_AC.Models.AC;
using WinForms = System.Windows.Forms;
using WinFormsIntegration = System.Windows.Forms.Integration;

namespace Street_Rod_AC;

public partial class CarRendererWindow : Window
{
    private ForwardKn5ObjectRenderer? _renderer;
    private WinForms.Panel? _renderPanel;
    private DispatcherTimer? _renderTimer;
    private System.Drawing.Point _lastMousePosition;
    private bool _isMouseDown;

    public CarRendererWindow(CarInfo carInfo)
    {
        InitializeComponent();
        Title = $"Car Viewer - {carInfo.Brand} {carInfo.Name}";

        try
        {
            // Create car description
            var carDesc = CarDescription.FromDirectory(carInfo.FolderPath);

            // Create renderer
            _renderer = new ForwardKn5ObjectRenderer(carDesc);
            _renderer.Width = 800;
            _renderer.Height = 600;
            _renderer.AutoRotate = false;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to create renderer:\n\n{ex.Message}",
                "Renderer Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
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

            // Setup camera
            if (_renderer.CameraOrbit != null)
            {
                _renderer.CameraOrbit.Radius = 5.0f;
                _renderer.CameraOrbit.Alpha = 0.0f;
                _renderer.CameraOrbit.Beta = 0.3f;
            }

            // Setup render loop
            _renderTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16) // ~60 FPS
            };
            _renderTimer.Tick += RenderTimer_Tick;
            _renderTimer.Start();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to initialize renderer:\n\n{ex.Message}",
                "Initialization Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
    }

    private void RenderTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            _renderer?.Draw();
        }
        catch (Exception ex)
        {
            _renderTimer?.Stop();
            System.Windows.MessageBox.Show($"Rendering error:\n\n{ex.Message}",
                "Render Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
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
