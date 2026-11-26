using System;
using System.Windows;

[Flags]
enum WindowResizeEdges { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

static class WindowResizeGeometry
{
    public const double MinPanelScale = 0.9, MaxPanelScale = 1.1;

    public static WindowResizeEdges FromHitTest(int hit) => hit switch
    {
        10 => WindowResizeEdges.Left, 11 => WindowResizeEdges.Right,
        12 => WindowResizeEdges.Top, 13 => WindowResizeEdges.Top | WindowResizeEdges.Left,
        14 => WindowResizeEdges.Top | WindowResizeEdges.Right, 15 => WindowResizeEdges.Bottom,
        16 => WindowResizeEdges.Bottom | WindowResizeEdges.Left,
        17 => WindowResizeEdges.Bottom | WindowResizeEdges.Right,
        _ => WindowResizeEdges.None
    };

    public static DwellTarget Target(WindowResizeEdges edges) => edges switch
    {
        WindowResizeEdges.Left => DwellTarget.ResizeLeft,
        WindowResizeEdges.Right => DwellTarget.ResizeRight,
        WindowResizeEdges.Top => DwellTarget.ResizeTop,
        WindowResizeEdges.Bottom => DwellTarget.ResizeBottom,
        WindowResizeEdges.Left | WindowResizeEdges.Top => DwellTarget.ResizeTopLeft,
        WindowResizeEdges.Right | WindowResizeEdges.Top => DwellTarget.ResizeTopRight,
        WindowResizeEdges.Left | WindowResizeEdges.Bottom => DwellTarget.ResizeBottomLeft,
        WindowResizeEdges.Right | WindowResizeEdges.Bottom => DwellTarget.ResizeBottomRight,
        _ => DwellTarget.None
    };

    public static WindowResizeEdges Edges(DwellTarget target) => target switch
    {
        DwellTarget.ResizeLeft => WindowResizeEdges.Left,
        DwellTarget.ResizeRight => WindowResizeEdges.Right,
        DwellTarget.ResizeTop => WindowResizeEdges.Top,
        DwellTarget.ResizeBottom => WindowResizeEdges.Bottom,
        DwellTarget.ResizeTopLeft => WindowResizeEdges.Top | WindowResizeEdges.Left,
        DwellTarget.ResizeTopRight => WindowResizeEdges.Top | WindowResizeEdges.Right,
        DwellTarget.ResizeBottomLeft => WindowResizeEdges.Bottom | WindowResizeEdges.Left,
        DwellTarget.ResizeBottomRight => WindowResizeEdges.Bottom | WindowResizeEdges.Right,
        _ => WindowResizeEdges.None
    };

    public static Rect Resize(Rect original, WindowResizeEdges edges, Vector delta, Size minimum)
    {
        double left = original.Left, top = original.Top, right = original.Right, bottom = original.Bottom;
        if (edges.HasFlag(WindowResizeEdges.Left)) left = Math.Min(left + delta.X, right - minimum.Width);
        if (edges.HasFlag(WindowResizeEdges.Right)) right = Math.Max(right + delta.X, left + minimum.Width);
        if (edges.HasFlag(WindowResizeEdges.Top)) top = Math.Min(top + delta.Y, bottom - minimum.Height);
        if (edges.HasFlag(WindowResizeEdges.Bottom)) bottom = Math.Max(bottom + delta.Y, top + minimum.Height);
        return new Rect(new Point(left, top), new Point(right, bottom));
    }

    public static double PanelScale(Size current, Size initial)
    {
        if (initial.Width <= 0 || initial.Height <= 0 || current.Width <= 0 || current.Height <= 0) return 1;
        double ratio = Math.Sqrt(current.Width / initial.Width * (current.Height / initial.Height));
        return Math.Clamp(1 + 0.2 * (ratio - 1), MinPanelScale, MaxPanelScale);
    }
}
