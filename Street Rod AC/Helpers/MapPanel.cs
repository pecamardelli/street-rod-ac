using System.Windows;

namespace Street_Rod_AC.Helpers;

/// <summary>
/// Shows a picture full-bleed with markers pinned to places on it.
///
/// The hard part is that those two pull against each other. Filling the window means cropping the picture by
/// however much the window does not match its shape, and a marker placed as a fraction of the window then
/// drifts off its place. Fitting the picture instead keeps the markers honest but leaves bars down the sides.
///
/// So neither: the picture is not stretched or fitted, the <b>crop is</b>. <see cref="Anchor"/> is the region
/// worth seeing; this widens or heightens it about its middle until it is exactly the shape of the panel, and
/// publishes the result as <see cref="EffectiveCrop"/> for the brush to take as its viewbox. There is map in
/// every direction, so a wider window simply shows more country. Because the panel knows precisely which part
/// of the picture is on screen, it can put every marker exactly on its spot.
///
/// A child carrying <c>MapPanel.MapX</c> and <c>MapPanel.MapY</c> - fractions of the <b>whole</b> picture -
/// is a marker, and is placed by its own middle. A child carrying neither is a layer and is given the whole
/// panel: the picture itself, a wash over it, the markers' own host.
/// </summary>
public class MapPanel : System.Windows.Controls.Panel
{
    public static readonly DependencyProperty MapXProperty = DependencyProperty.RegisterAttached(
        "MapX", typeof(double), typeof(MapPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty MapYProperty = DependencyProperty.RegisterAttached(
        "MapY", typeof(double), typeof(MapPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static double GetMapX(DependencyObject element) => (double)element.GetValue(MapXProperty);
    public static void SetMapX(DependencyObject element, double value) => element.SetValue(MapXProperty, value);

    public static double GetMapY(DependencyObject element) => (double)element.GetValue(MapYProperty);
    public static void SetMapY(DependencyObject element, double value) => element.SetValue(MapYProperty, value);

    public static readonly DependencyProperty AnchorProperty = DependencyProperty.Register(
        nameof(Anchor), typeof(Rect), typeof(MapPanel),
        new FrameworkPropertyMetadata(new Rect(0, 0, 1, 1), FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>
    /// The part of the picture that must be on screen, as fractions of it. What is actually shown grows out
    /// from here to whatever shape the window is.
    /// </summary>
    public Rect Anchor
    {
        get => (Rect)GetValue(AnchorProperty);
        set => SetValue(AnchorProperty, value);
    }

    public static readonly DependencyProperty ImagePixelWidthProperty = DependencyProperty.Register(
        nameof(ImagePixelWidth), typeof(double), typeof(MapPanel),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty ImagePixelHeightProperty = DependencyProperty.Register(
        nameof(ImagePixelHeight), typeof(double), typeof(MapPanel),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>
    /// The picture's own size. Needed because a crop measured in fractions is only square if the picture is:
    /// half the width of a wide picture is a lot more ground than half its height.
    /// </summary>
    public double ImagePixelWidth
    {
        get => (double)GetValue(ImagePixelWidthProperty);
        set => SetValue(ImagePixelWidthProperty, value);
    }

    public double ImagePixelHeight
    {
        get => (double)GetValue(ImagePixelHeightProperty);
        set => SetValue(ImagePixelHeightProperty, value);
    }

    private static readonly DependencyPropertyKey EffectiveCropPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(EffectiveCrop), typeof(Rect), typeof(MapPanel), new PropertyMetadata(new Rect(0, 0, 1, 1)));

    public static readonly DependencyProperty EffectiveCropProperty = EffectiveCropPropertyKey.DependencyProperty;

    /// <summary>
    /// The part of the picture on screen, once the anchor has been grown to the shape of the panel. Bind the
    /// image brush's viewbox to this and leave its stretch on Fill: the shapes already agree, so nothing is
    /// squashed and nothing is cut off unaccounted for.
    /// </summary>
    public Rect EffectiveCrop
    {
        get => (Rect)GetValue(EffectiveCropProperty);
        private set => SetValue(EffectiveCropPropertyKey, value);
    }

    /// <summary>
    /// Grows <paramref name="anchor"/> about its middle until it has the given shape, keeping it inside the
    /// picture. When it cannot grow far enough one way - a very wide window against a nearly square picture -
    /// it takes the whole of that side and gives back on the other, which is the only way to keep the shapes
    /// equal without stretching.
    /// </summary>
    private Rect Fit(Rect anchor, double panelAspect)
    {
        var imageWidth = ImagePixelWidth;
        var imageHeight = ImagePixelHeight;

        if (imageWidth <= 0 || imageHeight <= 0 || panelAspect <= 0 ||
            anchor.Width <= 0 || anchor.Height <= 0)
        {
            return anchor;
        }

        var centreX = anchor.X + anchor.Width / 2;
        var centreY = anchor.Y + anchor.Height / 2;

        // What shape the anchor is on the ground, rather than as fractions
        var anchorAspect = anchor.Width * imageWidth / (anchor.Height * imageHeight);

        var width = anchor.Width;
        var height = anchor.Height;

        if (panelAspect > anchorAspect)
        {
            width = height * imageHeight * panelAspect / imageWidth;
            if (width > 1.0)
            {
                width = 1.0;
                height = imageWidth / (panelAspect * imageHeight);
            }
        }
        else
        {
            height = width * imageWidth / (panelAspect * imageHeight);
            if (height > 1.0)
            {
                height = 1.0;
                width = panelAspect * imageHeight / imageWidth;
            }
        }

        width = Math.Min(1.0, width);
        height = Math.Min(1.0, height);

        // Slide back inside the picture rather than running off the edge of it
        var x = Math.Clamp(centreX - width / 2, 0.0, 1.0 - width);
        var y = Math.Clamp(centreY - height / 2, 0.0, 1.0 - height);

        return new Rect(x, y, width, height);
    }

    /// <summary>
    /// Whether a child has been given a place on the map. Asks where the value came from rather than reading
    /// it: a marker in a list gets its coordinates from the item container style, which is not a local value,
    /// so checking for one would call every pin a layer.
    /// </summary>
    private static bool IsMarker(DependencyObject child) =>
        DependencyPropertyHelper.GetValueSource(child, MapXProperty).BaseValueSource != BaseValueSource.Default ||
        DependencyPropertyHelper.GetValueSource(child, MapYProperty).BaseValueSource != BaseValueSource.Default;

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
    {
        var bounded = new System.Windows.Size(
            double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);

        // A marker sits on the panel rather than being packed into it and asks for what it wants; a layer is
        // offered the whole thing, because a picture asked how big it would like to be answers nothing.
        var unbounded = new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity);

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(IsMarker(child) ? unbounded : bounded);
        }

        return bounded;
    }

    protected override System.Windows.Size ArrangeOverride(System.Windows.Size finalSize)
    {
        if (finalSize.Width <= 0 || finalSize.Height <= 0) return finalSize;

        var crop = Fit(Anchor, finalSize.Width / finalSize.Height);
        EffectiveCrop = crop;

        foreach (UIElement child in InternalChildren)
        {
            if (!IsMarker(child))
            {
                child.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
                continue;
            }

            var size = child.DesiredSize;

            // From a place on the whole picture to a place on the part of it being shown
            var onScreenX = (GetMapX(child) - crop.X) / crop.Width;
            var onScreenY = (GetMapY(child) - crop.Y) / crop.Height;

            child.Arrange(new Rect(
                onScreenX * finalSize.Width - size.Width / 2,
                onScreenY * finalSize.Height - size.Height / 2,
                size.Width,
                size.Height));
        }

        return finalSize;
    }
}
