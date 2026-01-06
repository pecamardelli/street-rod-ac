using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Street_Rod_AC.Controls;

/// <summary>
/// Base window class providing custom chrome functionality
/// All application windows should inherit from this
/// </summary>
public class BaseWindow : Window
{
    private bool _isClosing = false;

    public BaseWindow()
    {
        // Apply the base window style
        var resourceDict = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Styles/Windows.xaml", UriKind.Absolute)
        };

        if (resourceDict["BaseWindowStyle"] is Style style)
        {
            Style = style;
        }

        // Set initial opacity for fade-in
        Opacity = 0;

        // Wire up events after template is applied
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Get template parts and wire up events
        if (Template?.FindName("PART_TitleBar", this) is Grid titleBar)
        {
            titleBar.MouseLeftButtonDown += TitleBar_MouseLeftButtonDown;
        }

        if (Template?.FindName("PART_MinimizeButton", this) is System.Windows.Controls.Button minButton)
        {
            minButton.Click += MinimizeButton_Click;
        }

        if (Template?.FindName("PART_MaximizeButton", this) is System.Windows.Controls.Button maxButton)
        {
            maxButton.Click += MaximizeButton_Click;
        }

        if (Template?.FindName("PART_CloseButton", this) is System.Windows.Controls.Button closeButton)
        {
            closeButton.Click += CloseButton_Click;
        }

        // Fade in animation
        var fadeIn = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromSeconds(0.3)
        };
        BeginAnimation(OpacityProperty, fadeIn);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // If already closing with animation, don't cancel
        if (_isClosing)
        {
            base.OnClosing(e);
            return;
        }

        // Cancel the close and start fade-out animation
        e.Cancel = true;
        _isClosing = true;

        var fadeOut = new DoubleAnimation
        {
            From = 1.0,
            To = 0.0,
            Duration = TimeSpan.FromSeconds(0.2)
        };

        fadeOut.Completed += (s, args) =>
        {
            // Actually close the window after fade-out
            // Keep _isClosing = true so the next OnClosing call doesn't cancel
            Close();
        };

        BeginAnimation(OpacityProperty, fadeOut);

        base.OnClosing(e);
    }

    /// <summary>
    /// Title bar drag to move window
    /// </summary>
    protected void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Double-click to maximize/restore
            MaximizeRestore();
        }
        else if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    /// <summary>
    /// Minimize button click
    /// </summary>
    protected void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    /// <summary>
    /// Maximize/Restore button click
    /// </summary>
    protected void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        MaximizeRestore();
    }

    /// <summary>
    /// Close button click
    /// </summary>
    protected void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Toggle between maximized and normal state
    /// </summary>
    private void MaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

        // Update maximize button icon
        UpdateMaximizeButtonIcon();
    }

    /// <summary>
    /// Update the maximize button icon based on window state
    /// </summary>
    private void UpdateMaximizeButtonIcon()
    {
        if (Template?.FindName("PART_MaximizeButton", this) is System.Windows.Controls.Button maxButton)
        {
            maxButton.Content = WindowState == WindowState.Maximized
                ? "\uE923" // Restore icon
                : "\uE922"; // Maximize icon
        }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        UpdateMaximizeButtonIcon();
    }
}
