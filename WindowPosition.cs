using System;
using System.Runtime.InteropServices;

// Use the same native screen coordinates as window dragging, including negative monitor positions.
record WindowPosition(int X, int Y);

static class WindowPositioning
{
    public static WindowPosition Capture(IntPtr window)
        => window != IntPtr.Zero && Native.GetWindowRect(window, out var rect)
            ? new WindowPosition(rect.Left, rect.Top) : null;

    public static WindowPosition Fit(WindowPosition position, int width, int height, Native.RECT area)
    {
        // Preserve intentional partial overlap at an edge or between screens, including the
        // invisible Windows frame. Reposition only when the top cannot be reached anymore.
        long visibleWidth = Math.Min((long)position.X + width, area.Right) - Math.Max(position.X, area.Left);
        long visibleTop = Math.Min((long)position.Y + Math.Min(height, 32), area.Bottom) - Math.Max(position.Y, area.Top);
        if (visibleWidth >= Math.Min(width, 80) && visibleTop >= Math.Min(height, 16)) return position;
        return new((int)Math.Clamp((long)position.X, area.Left, Math.Max((long)area.Left, (long)area.Right - width)),
                   (int)Math.Clamp((long)position.Y, area.Top, Math.Max((long)area.Top, (long)area.Bottom - height)));
    }

    public static bool Restore(IntPtr window, WindowPosition position)
    {
        if (position == null || !Native.GetWindowRect(window, out var rect)) return false;
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        var proposed = new Native.RECT
        {
            Left = position.X, Top = position.Y,
            Right = (int)Math.Min(int.MaxValue, (long)position.X + width),
            Bottom = (int)Math.Min(int.MaxValue, (long)position.Y + height)
        };
        if (!TryWorkArea(proposed, out var area)) return false;
        var visible = Fit(position, width, height, area);
        return Native.MoveWithoutActivation(window, visible.X, visible.Y);
    }

    public static bool TryWorkArea(Native.RECT proposed, out Native.RECT area)
    {
        var monitor = MonitorFromRect(ref proposed, 2); // MONITOR_DEFAULTTONEAREST
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        area = default;
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return false;
        area = info.WorkArea;
        return area.Right > area.Left && area.Bottom > area.Top;
    }

    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo
    {
        public int Size;
        public Native.RECT MonitorArea, WorkArea;
        public uint Flags;
    }
    [DllImport("user32.dll")] static extern IntPtr MonitorFromRect(ref Native.RECT rect, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
