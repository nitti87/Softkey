using System;

// Physical screen pixels, so monitor scaling and negative monitor origins need no conversion.
static class DragKeyScreenBounds
{
    public static Native.RECT Constrain(Native.RECT bounds, Native.RECT area)
    {
        long width = (long)bounds.Right - bounds.Left, height = (long)bounds.Bottom - bounds.Top;
        long availableWidth = (long)area.Right - area.Left, availableHeight = (long)area.Bottom - area.Top;
        if (width <= 0 || height <= 0 || availableWidth <= 0 || availableHeight <= 0) return bounds;
        width = Math.Min(width, availableWidth); height = Math.Min(height, availableHeight);
        long left = Math.Clamp((long)bounds.Left, area.Left, (long)area.Right - width);
        long top = Math.Clamp((long)bounds.Top, area.Top, (long)area.Bottom - height);
        return new Native.RECT { Left = (int)left, Top = (int)top, Right = (int)(left + width), Bottom = (int)(top + height) };
    }

    public static Native.RECT ConstrainToWorkArea(Native.RECT bounds)
        => WindowPositioning.TryWorkArea(bounds, out var area) ? Constrain(bounds, area) : bounds;
}
