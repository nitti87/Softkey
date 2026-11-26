using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

static class SoftKeyKeyDesign
{
    public const double LegendTextScale = 1.14;
    public static readonly CornerRadius Radius = new(6);
    public static readonly Brush FunctionFace = new SolidColorBrush(Color.FromRgb(0xE7, 0xEF, 0xF7));
    public static readonly Brush ModifierFace = new SolidColorBrush(Color.FromRgb(0xD1, 0xE0, 0xEE));
    public static readonly Brush AccentFace = new SolidColorBrush(Color.FromRgb(0xAE, 0xD5, 0xF2));
    public static readonly Brush Outline = new SolidColorBrush(Color.FromRgb(0x75, 0x87, 0x9A));
    static readonly Brush Shadow = new SolidColorBrush(Color.FromRgb(0xAE, 0xBC, 0xCC));

    public static FrameworkElementFactory Raised(FrameworkElementFactory cap, FrameworkElementFactory surface = null,
        CornerRadius? shadowRadius = null, Brush edgeBrush = null)
    {
        surface ??= new FrameworkElementFactory(typeof(Grid));
        surface.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
        var edge = new FrameworkElementFactory(typeof(Border));
        edge.SetValue(Border.BackgroundProperty, edgeBrush ?? Shadow);
        edge.SetValue(Border.CornerRadiusProperty, shadowRadius ?? Radius);
        edge.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 0));
        edge.SetValue(UIElement.IsHitTestVisibleProperty, false);
        surface.AppendChild(edge);
        cap.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 1));
        surface.AppendChild(cap);
        return surface;
    }
}

sealed class KeyboardKeyLegend : Viewbox
{
    const int MaxTextMeasurements = 4096;
    [ThreadStatic]
    static Dictionary<(string Text, CultureInfo Culture, FlowDirection Direction, Typeface Font, double Size, double Dpi), Size> textMeasurements;

    public static readonly DependencyProperty RequestedFontSizeProperty = DependencyProperty.Register(
        nameof(RequestedFontSize), typeof(double), typeof(KeyboardKeyLegend),
        new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double RequestedFontSize => (double)GetValue(RequestedFontSizeProperty);
    public Size NaturalTextSize { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is ContentPresenter { Content: string { Length: > 0 } text, TemplatedParent: Button key } presenter)
        {
            var font = new Typeface(key.FontFamily, key.FontStyle, key.FontWeight, key.FontStretch);
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var culture = CultureInfo.CurrentUICulture;
            var direction = key.FlowDirection;
            var cache = textMeasurements ??= new();
            Size MeasureText(double size)
            {
                var lookup = (text, culture, direction, font, size, pixelsPerDip);
                if (cache.TryGetValue(lookup, out Size measured)) return measured;
                // Foreground affects drawing, while these measurements depend only on the glyphs and their layout.
                var formatted = new FormattedText(text, culture, direction, font, size,
                    Brushes.Black, null, TextFormattingMode.Display, pixelsPerDip);
                measured = new Size(formatted.WidthIncludingTrailingWhitespace, formatted.Height);
                if (cache.Count >= MaxTextMeasurements) cache.Clear();
                cache.Add(lookup, measured);
                return measured;
            }

            double requested = RequestedFontSize;
            NaturalTextSize = MeasureText(requested);
            double requestedPixels = requested * pixelsPerDip;
            double adjusted = requestedPixels is >= 1 and <= 13
                ? Math.Round(requestedPixels, MidpointRounding.AwayFromZero) / pixelsPerDip : requested;
            var metrics = adjusted == requested ? NaturalTextSize : MeasureText(adjusted);
            // Fit narrower labels in actual font pixels so WPF can hint the glyphs at that size.
            while ((metrics.Width > availableSize.Width || metrics.Height > availableSize.Height) && adjusted * pixelsPerDip > 1)
            {
                double widthRatio = metrics.Width > 0 ? availableSize.Width / metrics.Width : double.PositiveInfinity;
                double ratio = Math.Min(widthRatio, availableSize.Height / metrics.Height);
                double pixels = Math.Floor(Math.Min(adjusted * pixelsPerDip - 1, adjusted * pixelsPerDip * ratio));
                adjusted = Math.Max(1, pixels) / pixelsPerDip;
                metrics = MeasureText(adjusted);
            }
            TextElement.SetFontSize(presenter, adjusted);
        }
        return base.MeasureOverride(availableSize);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateMeasure();
    }
}

sealed class KeyboardKeySurface : Panel
{
    public static readonly DependencyProperty InsetProperty = DependencyProperty.RegisterAttached(
        "Inset", typeof(Size), typeof(KeyboardKeySurface),
        new FrameworkPropertyMetadata(new Size(), FrameworkPropertyMetadataOptions.AffectsArrange));

    protected override Size MeasureOverride(Size availableSize)
    {
        var desired = new Size();
        foreach (UIElement part in InternalChildren)
        {
            part.Measure(availableSize);
            desired.Width = Math.Max(desired.Width, part.DesiredSize.Width);
            desired.Height = Math.Max(desired.Height, part.DesiredSize.Height);
        }
        return desired;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double insetX = 0, insetY = 0;
        var requested = (Size)GetValue(InsetProperty);
        if ((requested.Width > 0 || requested.Height > 0) && InternalChildren[1] is Border face
            && face.Child is KeyboardKeyLegend { Child: ContentPresenter { Content: string { Length: > 0 } } } legend)
        {
            // Preserve the legend's full-size fit and a pixel of clearance when shrinking the face.
            Thickness border = face.BorderThickness, padding = legend.Margin, shadow = face.Margin;
            double width = legend.NaturalTextSize.Width + border.Left + border.Right + padding.Left + padding.Right
                + shadow.Left + shadow.Right;
            double height = legend.NaturalTextSize.Height + border.Top + border.Bottom + padding.Top + padding.Bottom
                + shadow.Top + shadow.Bottom;
            double room = Math.Min(finalSize.Width - width, finalSize.Height - height);
            var dpi = VisualTreeHelper.GetDpi(this);
            double scale = Math.Min(dpi.DpiScaleX, dpi.DpiScaleY);
            double safeInset = Math.Max(0, room / 2 - 1 / scale);
            insetX = Math.Floor(Math.Min(requested.Width, safeInset) * scale) / scale;
            insetY = Math.Floor(Math.Min(requested.Height, safeInset) * scale) / scale;
        }
        var faceBounds = new Rect(insetX, insetY, finalSize.Width - 2 * insetX, finalSize.Height - 2 * insetY);
        foreach (UIElement part in InternalChildren)
            part.Arrange(faceBounds);
        return finalSize;
    }
}
