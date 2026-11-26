using System;

static class VerticalWindowSnap
{
    public static Native.RECT JumpToEdge(Native.RECT bounds, Native.RECT area)
    {
        var fitted = Constrain(bounds, area);
        var above = (long)fitted.Top - area.Top;
        var below = (long)area.Bottom - fitted.Bottom;
        int height = fitted.Bottom - fitted.Top;
        fitted.Top = below == 0 || (above > 0 && above <= below) ? area.Top : area.Bottom - height;
        fitted.Bottom = fitted.Top + height;
        return fitted;
    }

    // Only the top/bottom snap; all four sides are bounded by the monitor's usable area.
    public static Native.RECT Constrain(Native.RECT bounds, Native.RECT area, bool resizing = false)
    {
        long available = (long)area.Bottom - area.Top;
        long height = (long)bounds.Bottom - bounds.Top;
        if (available <= 0 || height <= 0 || area.Right <= area.Left || bounds.Right <= bounds.Left) return bounds;
        var (left, right) = LimitAxis(bounds.Left, bounds.Right, area.Left, area.Right, resizing);
        var (top, bottom) = LimitAxis(bounds.Top, bounds.Bottom, area.Top, area.Bottom, resizing);
        if (!resizing)
        {
            long toTop = top - area.Top, toBottom = area.Bottom - bottom;
            if (Math.Min(toTop, toBottom) * 100 <= available) // 1% of usable height.
            {
                height = bottom - top;
                top = toTop <= toBottom ? area.Top : area.Bottom - height;
                bottom = top + height;
            }
        }
        return new Native.RECT { Left = (int)left, Right = (int)right, Top = (int)top, Bottom = (int)bottom };
    }

    static (long Start, long End) LimitAxis(long start, long end, long minimum, long maximum, bool resizing)
    {
        // A resize stops the selected edge, preserving the opposite edge.
        if (resizing && end > minimum && start < maximum)
            return (Math.Max(start, minimum), Math.Min(end, maximum));
        long size = Math.Min(end - start, maximum - minimum);
        long bounded = Math.Clamp(start, minimum, maximum - size);
        return (bounded, bounded + size);
    }
}
