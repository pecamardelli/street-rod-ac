using System.Windows;

namespace Street_Rod_AC.Helpers;

/// <summary>
/// Gives every child the same rectangle: the largest one of a fixed shape that fits, centred.
///
/// A picture and the markers pinned to it have to agree on where its edges are. Stretching the picture to
/// fill the screen would crop it by however much the window happens not to match, and the markers would
/// drift off their places. This hands both the same rectangle instead, and leaves the rest of the screen to
/// whatever is behind.
/// </summary>
public class AspectPanel : System.Windows.Controls.Panel
{
    public static readonly DependencyProperty AspectProperty = DependencyProperty.Register(
        nameof(Aspect), typeof(double), typeof(AspectPanel),
        new FrameworkPropertyMetadata(1.0,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>The shape to keep, as width over height</summary>
    public double Aspect
    {
        get => (double)GetValue(AspectProperty);
        set => SetValue(AspectProperty, value);
    }

    private System.Windows.Size Fit(System.Windows.Size available)
    {
        var aspect = Aspect;
        if (double.IsNaN(aspect) || aspect <= 0) aspect = 1.0;

        var width = available.Width;
        var height = available.Height;
        if (double.IsInfinity(width) || double.IsInfinity(height) || width <= 0 || height <= 0)
        {
            return new System.Windows.Size(0, 0);
        }

        // Whichever way round it is, the picture has to stay inside
        if (width / height > aspect)
        {
            width = height * aspect;
        }
        else
        {
            height = width / aspect;
        }

        return new System.Windows.Size(width, height);
    }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
    {
        var fitted = Fit(availableSize);
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(fitted);
        }

        return new System.Windows.Size(
            double.IsInfinity(availableSize.Width) ? fitted.Width : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? fitted.Height : availableSize.Height);
    }

    protected override System.Windows.Size ArrangeOverride(System.Windows.Size finalSize)
    {
        var fitted = Fit(finalSize);
        var left = (finalSize.Width - fitted.Width) / 2;
        var top = (finalSize.Height - fitted.Height) / 2;

        foreach (UIElement child in InternalChildren)
        {
            child.Arrange(new Rect(left, top, fitted.Width, fitted.Height));
        }

        return finalSize;
    }
}
