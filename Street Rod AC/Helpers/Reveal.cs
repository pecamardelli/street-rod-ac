using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Street_Rod_AC.Helpers;

/// <summary>
/// Visibility with a transition: an element bound to <c>Reveal.IsShown</c> fades and slides in from an offset,
/// and goes out the same way before it collapses. Use it in place of a Visibility binding.
/// </summary>
public static class Reveal
{
    private static readonly TimeSpan InDuration = TimeSpan.FromMilliseconds(320);
    private static readonly TimeSpan OutDuration = TimeSpan.FromMilliseconds(200);

    public static readonly DependencyProperty IsShownProperty = DependencyProperty.RegisterAttached(
        "IsShown", typeof(bool), typeof(Reveal), new PropertyMetadata(true, OnIsShownChanged));

    /// <summary>Where the element comes in from, and leaves to, relative to its place</summary>
    public static readonly DependencyProperty OffsetXProperty = DependencyProperty.RegisterAttached(
        "OffsetX", typeof(double), typeof(Reveal), new PropertyMetadata(0.0));

    public static readonly DependencyProperty OffsetYProperty = DependencyProperty.RegisterAttached(
        "OffsetY", typeof(double), typeof(Reveal), new PropertyMetadata(0.0));

    public static bool GetIsShown(DependencyObject element) => (bool)element.GetValue(IsShownProperty);
    public static void SetIsShown(DependencyObject element, bool value) => element.SetValue(IsShownProperty, value);

    public static double GetOffsetX(DependencyObject element) => (double)element.GetValue(OffsetXProperty);
    public static void SetOffsetX(DependencyObject element, double value) => element.SetValue(OffsetXProperty, value);

    public static double GetOffsetY(DependencyObject element) => (double)element.GetValue(OffsetYProperty);
    public static void SetOffsetY(DependencyObject element, double value) => element.SetValue(OffsetYProperty, value);

    private static void OnIsShownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;

        var show = (bool)e.NewValue;

        // Whatever state a screen opens in is simply there: transitions are for changes one watches happen
        if (!element.IsLoaded)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            element.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        var slide = element.RenderTransform as TranslateTransform;
        if (slide == null || slide.IsFrozen) element.RenderTransform = slide = new TranslateTransform();

        var (offsetX, offsetY) = (GetOffsetX(element), GetOffsetY(element));
        if (show)
        {
            // Coming in from wherever it is: a half-finished exit turns around instead of jumping
            var fromHidden = element.Visibility != Visibility.Visible;
            element.Visibility = Visibility.Visible;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(fromHidden ? 0 : element.Opacity, 1, InDuration) { EasingFunction = ease });
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromHidden ? offsetX : slide.X, 0, InDuration) { EasingFunction = ease });
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromHidden ? offsetY : slide.Y, 0, InDuration) { EasingFunction = ease });
        }
        else
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var fade = new DoubleAnimation(element.Opacity, 0, OutDuration) { EasingFunction = ease };
            fade.Completed += (_, _) =>
            {
                // Shown again in the meantime: leave it alone
                if (!GetIsShown(element)) element.Visibility = Visibility.Collapsed;
            };

            element.BeginAnimation(UIElement.OpacityProperty, fade);
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(slide.X, offsetX, OutDuration) { EasingFunction = ease });
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(slide.Y, offsetY, OutDuration) { EasingFunction = ease });
        }
    }
}
