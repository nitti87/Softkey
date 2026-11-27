using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

static class DragKeySymbols
{
    public const double Width = 28, Height = 28;
    const double ArtworkSpan = 23;
    static readonly DrawingImage[] Active = Enum.GetValues<DragKeySymbol>().Select(symbol => Draw(symbol, false)).ToArray();
    static readonly DrawingImage[] Paused = Enum.GetValues<DragKeySymbol>().Select(symbol => Draw(symbol, true)).ToArray();
    static readonly DrawingImage[] Compact = Enum.GetValues<DragKeySymbol>().Select(symbol => Draw(symbol, false, compact: true)).ToArray();
    static readonly DrawingImage[] CompactPaused = Enum.GetValues<DragKeySymbol>().Select(symbol => Draw(symbol, true, compact: true)).ToArray();

    public static DrawingImage Image(DragKeySymbol symbol, bool paused = false, bool compact = false)
        => (compact ? paused ? CompactPaused : Compact : paused ? Paused : Active)[(int)symbol];

    static DrawingImage Draw(DragKeySymbol symbol, bool paused, bool compact = false)
    {
        Brush Paint(Color color)
        {
            if (!paused) return new SolidColorBrush(color);
            double luma = 0.21 * color.R + 0.72 * color.G + 0.07 * color.B;
            return new SolidColorBrush(Color.FromRgb(
                (byte)(0.52 * luma + 0.48 * 212),
                (byte)(0.52 * luma + 0.48 * 208),
                (byte)(0.52 * luma + 0.48 * 200)));
        }

        var ink = Paint(Color.FromRgb(0x2D, 0x2A, 0x26));
        var accent = Paint(Color.FromRgb(0x35, 0x67, 0x91));
        var softKeyFill = Paint(Color.FromRgb(0x52, 0x4E, 0x48));
        var softSurface = Paint(Color.FromRgb(0xE8, 0xE5, 0xDE));
        var cardSurface = Paint(Color.FromRgb(0xFC, 0xFB, 0xF9));
        Pen Line(Brush color, double width) => new(color, width)
        {
            StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round
        };
        var outline = Line(ink, 1.85);

        var artwork = new DrawingGroup();
        using (var drawing = artwork.Open())
        {
            void Path(string data, Brush fill = null, Pen stroke = null)
                => drawing.DrawGeometry(fill, stroke, Geometry.Parse(data));

            switch (symbol)
            {
                case DragKeySymbol.AutoClick:
                    Path("M 5,15 C 2.7,12.8 2.4,9.6 3.8,7 C 5.3,4.1 8.3,2.9 11.1,3.6 Q 14.2,4.2 15.7,6.8",
                        stroke: Line(accent, compact ? 2.2 : 1.85));
                    Path("M 10.35,8.1 Q 10.3,7.45 10.9,7.72 L 24.4,14.6 Q 25,14.9 24.4,15.1 "
                        + "L 18.5,16.9 L 21.75,22.25 Q 22,22.7 21.56,23 L 19.5,24.2 Q 19.05,24.45 18.76,23.98 "
                        + "L 15.58,18.7 L 11.93,22 Q 11.43,22.43 11.38,21.74 Z",
                        cardSurface, Line(ink, compact ? 2.0 : 1.65));
                    break;

                case DragKeySymbol.Keyboard:
                    drawing.DrawRoundedRectangle(softSurface, outline, new Rect(2.5, 6.1, 23, 15.8), 3, 3);
                    int columns = compact ? 4 : 5;
                    for (int row = 0; row < 2; row++)
                    for (int column = 0; column < columns; column++)
                        drawing.DrawRoundedRectangle(softKeyFill, null,
                            new Rect(5.2 + column * (compact ? 4.8 : 3.7), 9.3 + row * 3.4,
                                compact ? 3.2 : 2.7, compact ? 2.5 : 2.15), 0.5, 0.5);
                    drawing.DrawRoundedRectangle(accent, null, new Rect(8.6, 16.3, 10.8, 2.2), 0.55, 0.55);
                    break;

                case DragKeySymbol.WindowHeader:
                    var header = new Rect(4, 4.9, 20, 18.2);
                    drawing.DrawRoundedRectangle(cardSurface, null, header, 3, 3);
                    Path("M 4,11.3 V 7.9 Q 4,4.9 7,4.9 H 21 Q 24,4.9 24,7.9 V 11.3 Z", accent);
                    Path("M 4,11.3 H 24", stroke: Line(ink, compact ? 2 : 1.4));
                    drawing.DrawRoundedRectangle(null, outline, header, 3, 3);
                    break;

                case DragKeySymbol.KeyboardToTop:
                case DragKeySymbol.KeyboardToBottom:
                    drawing.DrawRoundedRectangle(cardSurface, outline, new Rect(2.8, 3.5, 22.4, 17.8), 2.6, 2.6);
                    Path("M 14,21.4 V 24.8 M 9.5,25 H 18.5", stroke: Line(ink, 1.8));
                    double keyY = symbol == DragKeySymbol.KeyboardToTop ? 5.4 : 13.3;
                    drawing.DrawRoundedRectangle(accent, Line(ink, 1.1), new Rect(5.4, keyY, 17.2, 6.3), 1.35, 1.35);
                    for (int col = 0; col < (compact ? 3 : 4); col++)
                        drawing.DrawRoundedRectangle(cardSurface, null,
                            new Rect(7.6 + col * (compact ? 4.65 : 3.45), keyY + (compact ? 2 : 1.4),
                                compact ? 3.3 : 2.35, compact ? 2.25 : 1.35), 0.35, 0.35);
                    if (!compact)
                        drawing.DrawRoundedRectangle(cardSurface, null, new Rect(9.5, keyY + 3.8, 9, 1.1), 0.35, 0.35);
                    break;

                case DragKeySymbol.Undock:
                    drawing.DrawRoundedRectangle(softSurface, outline, new Rect(4.1, 7.7, 8.3, 15.3), 2.1, 2.1);
                    drawing.DrawRoundedRectangle(cardSurface, outline, new Rect(15.7, 4.9, 8.3, 15.3), 2.1, 2.1);
                    Path("M 7.3,12.1 H 9.2 M 7.3,15.3 H 9.2", stroke: Line(accent, 1.5));
                    break;

                case DragKeySymbol.Power:
                    Path("M 8.7,7.8 C 5.9,9.6 4.6,12.3 4.6,15.4 C 4.6,20.6 8.8,24.8 14,24.8 "
                        + "C 19.2,24.8 23.4,20.6 23.4,15.4 C 23.4,12.3 22.1,9.6 19.3,7.8", stroke: Line(ink, 2.3));
                    Path("M 14,3.9 V 14.5", stroke: Line(accent, 2.55));
                    break;

                default:
                    DrawMouse();
                    break;
            }

            void DrawMouse()
            {
                var roleAccent = Paint(symbol switch
                {
                    DragKeySymbol.RightClick => Color.FromRgb(0x5A, 0x8A, 0x78),
                    DragKeySymbol.DoubleClick => Color.FromRgb(0xC9, 0x9A, 0x3B),
                    _ => Color.FromRgb(0xB8, 0x5A, 0x4B)
                });
                bool middle = symbol == DragKeySymbol.MiddleDrag;
                bool right = symbol is DragKeySymbol.RightClick or DragKeySymbol.RightDrag;
                const string body = "M 5.8,11.4 C 5.8,6 8.6,2.6 14,2.6 C 19.4,2.6 22.2,6 22.2,11.4 V 16.6 "
                    + "C 22.2,22 19.4,25.4 14,25.4 C 8.6,25.4 5.8,22 5.8,16.6 Z";
                Path(body, cardSurface);
                if (!middle)
                    Path(right ? "M 14,2.6 C 19.4,2.6 22.2,6 22.2,11.4 V 12.7 H 14 Z"
                        : "M 5.8,11.4 C 5.8,6 8.6,2.6 14,2.6 V 12.7 H 5.8 Z", roleAccent);
                Path("M 5.8,12.7 H 22.2 M 14,2.6 V 12.7", stroke: Line(ink, compact ? 2 : 1.48));
                Path(body, stroke: outline);
                if (middle)
                    drawing.DrawRoundedRectangle(roleAccent, Line(ink, compact ? 1.5 : 1.15),
                        compact ? new Rect(11.1, 4.6, 5.8, 7.15) : new Rect(11.45, 4.9, 5.1, 6.5), 2.5, 2.5);
            }
        }
        if (compact)
        {
            artwork.GuidelineSet = symbol switch
            {
                DragKeySymbol.RightClick or DragKeySymbol.DoubleClick or DragKeySymbol.LeftDrag
                    or DragKeySymbol.RightDrag or DragKeySymbol.MiddleDrag
                    => new GuidelineSet(new[] { 13.0, 15.0 }, new[] { 11.7, 13.7 }),
                DragKeySymbol.WindowHeader => new GuidelineSet(Array.Empty<double>(), new[] { 10.3, 12.3 }),
                _ => null
            };
        }
        // Fit the visible strokes, rather than the source canvas, to the same centered area.
        var bounds = artwork.Bounds;
        double scale = ArtworkSpan / Math.Max(bounds.Width, bounds.Height);
        artwork.Transform = new MatrixTransform(scale, 0, 0, scale,
            (Width - bounds.Width * scale) / 2 - bounds.Left * scale,
            (Height - bounds.Height * scale) / 2 - bounds.Top * scale);
        var canvas = new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, Width, Height)));
        var result = new DrawingImage(new DrawingGroup { Children = { canvas, artwork } });
        result.Freeze();
        return result;
    }
}

sealed class DragKeyIconImage(DragKeySymbol symbol) : System.Windows.Controls.Image
{
    internal const double CompactPixelLimit = 20.5;
    internal bool UsesCompactArtwork { get; private set; }

    protected override void OnRender(DrawingContext drawing)
    {
        if (Source == null) return;
        var source = PresentationSource.FromVisual(this);
        if (source?.RootVisual is not Visual root || source.CompositionTarget == null)
        {
            base.OnRender(drawing);
            return;
        }

        var local = new Rect(RenderSize);
        var physical = TransformToAncestor(root).TransformBounds(local);
        physical.Transform(source.CompositionTarget.TransformToDevice);
        UsesCompactArtwork = Math.Min(physical.Width, physical.Height) < CompactPixelLimit;
        bool paused = ReferenceEquals(Source, DragKeySymbols.Image(symbol, paused: true));
        var image = DragKeySymbols.Image(symbol, paused, UsesCompactArtwork);
        var guides = new GuidelineSet(new[] { local.Left, local.Right }, new[] { local.Top, local.Bottom });
        drawing.PushGuidelineSet(guides);
        drawing.DrawImage(image, local);
        drawing.Pop();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateVisual();
    }
}

