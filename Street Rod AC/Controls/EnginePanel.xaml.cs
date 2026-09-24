using System.ComponentModel;
using System.Windows;
using TextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;
using System.Windows.Input;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Street_Rod_AC.Audio;

namespace Street_Rod_AC.Controls;

/// <summary>
/// Start a car's engine where it stands and rev it: a start button, a tach and a pedal. The pedal is held with the
/// mouse (the lower the press, the further down), or W / Space push it to the floor wherever the focus is on the
/// window. Bind <see cref="Engine"/> to the car's engine, and a viewport's RunningEngine to <see cref="Runner"/>
/// so the body rocks with it.
/// </summary>
public partial class EnginePanel : System.Windows.Controls.UserControl
{
    private Window? _window;

    public EnginePanel()
    {
        Runner = new EngineRunner();
        InitializeComponent();

        Runner.PropertyChanged += OnRunnerChanged;
        TachTrack.SizeChanged += (_, _) => UpdateTach();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        // Out of sight, out of earshot: nobody can reach the stop button of a panel that is not on screen
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) Runner.StopNow();
        };
    }

    /// <summary>The engine under the pedal</summary>
    public EngineRunner Runner { get; }

    public static readonly DependencyProperty EngineProperty = DependencyProperty.Register(
        nameof(Engine), typeof(EngineSpec), typeof(EnginePanel),
        new PropertyMetadata(null, (d, e) => ((EnginePanel)d).OnEngineChanged()));

    /// <summary>The car's engine; null when there is no car, or it has no engine</summary>
    public EngineSpec? Engine
    {
        get => (EngineSpec?)GetValue(EngineProperty);
        set => SetValue(EngineProperty, value);
    }

    private void OnEngineChanged()
    {
        if (IsLoaded) _ = Runner.SetEngineAsync(Engine);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        if (_window != null)
        {
            _window.PreviewKeyDown += OnWindowKeyDown;
            _window.PreviewKeyUp += OnWindowKeyUp;
            _window.Deactivated += OnWindowDeactivated;
        }

        _ = Runner.SetEngineAsync(Engine);
        UpdateTach();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_window != null)
        {
            _window.PreviewKeyDown -= OnWindowKeyDown;
            _window.PreviewKeyUp -= OnWindowKeyUp;
            _window.Deactivated -= OnWindowDeactivated;
            _window = null;
        }

        // The screen is going: silence, and let go of the bank so a race can move it
        Runner.Detach();
        _ = EngineAudio.Shared.UnloadAllAsync();
    }

    private void OnRunnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EngineRunner.RpmFraction) or nameof(EngineRunner.RedlineFraction)) UpdateTach();
    }

    private void UpdateTach()
    {
        var width = TachTrack.ActualWidth;
        Needle.Width = width;
        RedZone.Width = Math.Max(0, width * (1 - Runner.RedlineFraction));
        Needle.Fill = Runner.RpmFraction >= Runner.RedlineFraction - 0.005
            ? Brushes.Red
            : (Brush)FindResource("AccentPrimaryBrush");
    }

    private void OnStartClick(object sender, RoutedEventArgs e) => Runner.Toggle();

    #region Throttle

    private static bool IsThrottleKey(Key key) => key is Key.W or Key.Space;

    /// <summary>Typing in a box is not revving the engine</summary>
    private static bool IsTyping() => Keyboard.FocusedElement is TextBoxBase or System.Windows.Controls.PasswordBox;

    private void OnWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!IsVisible || !Runner.HasEngine || !IsThrottleKey(e.Key) || IsTyping()) return;

        Runner.SetKey(true);

        // Space would otherwise press whatever button has the focus
        e.Handled = true;
    }

    private void OnWindowKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!IsThrottleKey(e.Key)) return;

        Runner.SetKey(false);
        if (IsVisible && Runner.HasEngine) e.Handled = true;
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        // A key let go in another window never comes back here
        Runner.SetKey(false);
        Runner.SetPedal(null);
    }

    private void OnPedalDown(object sender, MouseButtonEventArgs e)
    {
        Pedal.CaptureMouse();
        PressPedal(e.GetPosition(Pedal));
        e.Handled = true;
    }

    private void OnPedalMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (Pedal.IsMouseCaptured) PressPedal(e.GetPosition(Pedal));
    }

    private void OnPedalUp(object sender, MouseButtonEventArgs e)
    {
        Pedal.ReleaseMouseCapture();
        Runner.SetPedal(null);
        e.Handled = true;
    }

    private void OnPedalLost(object sender, System.Windows.Input.MouseEventArgs e) => Runner.SetPedal(null);

    // Top of the pedal is a touch, the bottom is the floor; dragging off the ends holds the end
    private void PressPedal(System.Windows.Point position)
    {
        var height = Math.Max(1, Pedal.ActualHeight);
        Runner.SetPedal(Math.Clamp(0.15 + 0.85 * position.Y / height, 0, 1));
    }

    #endregion
}
