using System.Windows;

namespace Street_Rod_AC.Helpers;

/// <summary>
/// Lays its children out at a fraction of its own size rather than at a pixel offset, so a marker put on a
/// picture stays on the same spot of the picture whatever the window does.
///
/// A child is placed by its own middle: <c>FractionPanel.X="0.5" FractionPanel.Y="0.5"</c> is the centre.
/// </summary>
public class FractionPanel : System.Windows.Controls.Panel
{
    public static readonly DependencyProperty XProperty = DependencyProperty.RegisterAttached(
        "X", typeof(double), typeof(FractionPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty YProperty = DependencyProperty.RegisterAttached(
        "Y", typeof(double), typeof(FractionPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static double GetX(DependencyObject element) => (double)element.GetValue(XProperty);
    public static void SetX(DependencyObject element, double value) => element.SetValue(XProperty, value);

    public static double GetY(DependencyObject element) => (double)element.GetValue(YProperty);
    public static void SetY(DependencyObject element, double value) => element.SetValue(YProperty, value);

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
    {
        // Children are placed on the panel, not packed into it: let each one ask for what it wants
        var unbounded = new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity);
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(unbounded);
        }

        // Nothing here dictates the panel's size; it takes what it is given
        return new System.Windows.Size(
            double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
    }

    protected override System.Windows.Size ArrangeOverride(System.Windows.Size finalSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            var size = child.DesiredSize;
            var x = GetX(child) * finalSize.Width - size.Width / 2;
            var y = GetY(child) * finalSize.Height - size.Height / 2;
            child.Arrange(new Rect(x, y, size.Width, size.Height));
        }

        return finalSize;
    }
}
