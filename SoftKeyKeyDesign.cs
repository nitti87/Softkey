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
    public static readonly CornerRadius Radius = new(7);
    public static readonly Brush WindowSurface = new SolidColorBrush(Color.FromRgb(0xEC, 0xEA, 0xE4));
    public static readonly Brush TraySurface = new SolidColorBrush(Color.FromRgb(0xDC, 0xD8, 0xCE));
    public static readonly Brush LetterFace = new SolidColorBrush(Color.FromRgb(0xFD, 0xFC, 0xFA));
    public static readonly Brush FunctionFace = new SolidColorBrush(Color.FromRgb(0xE8, 0xE5, 0xDE));
    public static readonly Brush ModifierFace = new SolidColorBrush(Color.FromRgb(0xE0, 0xDD, 0xD5));
    public static readonly Brush AccentFace = new SolidColorBrush(Color.FromRgb(0xD6, 0xE3, 0xEE));
    public static readonly Brush EnterFace = new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x7C));
    public static readonly Brush EnterHover = new SolidColorBrush(Color.FromRgb(0x24, 0x4B, 0x6C));
    public static readonly Brush EnterPressed = new SolidColorBrush(Color.FromRgb(0x1D, 0x3E, 0x5A));
    public static readonly Brush EnterBorder = new SolidColorBrush(Color.FromRgb(0x22, 0x46, 0x65));
    public static readonly Brush Outline = new SolidColorBrush(Color.FromRgb(0xB5, 0xB0, 0xA4));
    public static readonly Brush Shadow = new SolidColorBrush(Color.FromRgb(0xA5, 0xA0, 0x94));
    public static readonly Brush PrimaryText = new SolidColorBrush(Color.FromRgb(0x23, 0x21, 0x1E));
    public static readonly Brush SecondaryText = new SolidColorBrush(Color.FromRgb(0x3D, 0x3A, 0x35));
    public static readonly Brush SlateAccent = new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x7C));
    public static readonly Brush SlateOutline = new SolidColorBrush(Color.FromRgb(0x6E, 0x94, 0xB4));
    public static readonly Brush SlateAccentText = new SolidColorBrush(Color.FromRgb(0x1E, 0x3E, 0x5A));
    public static readonly Brush KeyHover = new SolidColorBrush(Color.FromRgb(0xDC, 0xE5, 0xED));
    public static readonly Brush KeyFlash = new SolidColorBrush(Color.FromRgb(0xC9, 0xD9, 0xE6));
    public static readonly Brush KeyHeld = new SolidColorBrush(Color.FromRgb(0xD6, 0xE3, 0xEE));
    public static readonly Brush HeaderHover = new SolidColorBrush(Color.FromRgb(0xE2, 0xDF, 0xD8));
    public static readonly Brush MutedDivider = new SolidColorBrush(Color.FromRgb(0xCF, 0xCB, 0xC2));

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
