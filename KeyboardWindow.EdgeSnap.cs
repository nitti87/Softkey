using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

partial class KeyboardWindow
{
    [StructLayout(LayoutKind.Sequential)] struct WindowPositionChange
    {
        public IntPtr Window, InsertAfter;
        public int X, Y, Width, Height;
        public uint Flags;
    }

    static Native.RECT ConstrainKeyboardBounds(Native.RECT proposed, bool resizing = false)
        => WindowPositioning.TryWorkArea(proposed, out var area)
            ? VerticalWindowSnap.Constrain(proposed, area, resizing) : proposed;

    static bool TryKeyboardEdgeDestination(IntPtr window, out Native.RECT bounds, out bool toTop)
    {
        bounds = default;
        toTop = false;
        if (window == IntPtr.Zero || !Native.GetWindowRect(window, out var current)
            || !WindowPositioning.TryWorkArea(current, out var area)) return false;
        bounds = VerticalWindowSnap.JumpToEdge(current, area);
        toTop = bounds.Top == area.Top;
        return true;
    }

    void RefreshKeyboardMoveDestination()
    {
        if (secureMode || dragKeyWin == null || !IsVisible || WindowState != WindowState.Normal
            || !TryKeyboardEdgeDestination(new WindowInteropHelper(this).Handle, out _, out bool toTop)) return;
        dragKeyWin.SetKeyboardMoveDestination(toTop);
        dragKeyDockPreview.SetKeyboardMoveDestination(toTop);
    }

    void MoveKeyboardToScreenEdge()
    {
        if (secureMode || desktopSessionSuspended || mainWindowActionsLocked || !cfg.AutoClick
            || mouseActions.IsDragging || !IsVisible || WindowState != WindowState.Normal) return;
        var window = new WindowInteropHelper(this).Handle;
        if (!TryKeyboardEdgeDestination(window, out var bounds, out _) || !CancelMouseAction()) return;
        CloseMenu();
        ReportInputResult(Native.ResizeWithoutActivation(window, bounds.Left, bounds.Top,
            bounds.Right - bounds.Left, bounds.Bottom - bounds.Top));
        // A docked button may have moved out from under the pointer.
        SuppressCurrentDwell();
    }

    void ApplyKeyboardEdges()
    {
        if (WindowState != WindowState.Normal) return;
        var window = new WindowInteropHelper(this).Handle;
        if (!Native.GetWindowRect(window, out var current)) return;
        var bounded = ConstrainKeyboardBounds(current);
        if (bounded.Left == current.Left && bounded.Right == current.Right
            && bounded.Top == current.Top && bounded.Bottom == current.Bottom) return;
        Native.ResizeWithoutActivation(window, bounded.Left, bounded.Top,
            bounded.Right - bounded.Left, bounded.Bottom - bounded.Top);
    }

    bool FilterKeyboardEdges(IntPtr window, int message, IntPtr data)
    {
        if (!positionTrackingReady || WindowState != WindowState.Normal || Native.IsMinimized(window) || data == IntPtr.Zero) return false;
        if (message is 0x0216 or 0x0214) // WM_MOVING / WM_SIZING: constrain the proposed drag outline.
        {
            var proposed = Marshal.PtrToStructure<Native.RECT>(data);
            var bounded = ConstrainKeyboardBounds(proposed, message == 0x0214);
            Marshal.StructureToPtr(bounded, data, false);
            return true;
        }
        if (message != 0x0046) return false; // WM_WINDOWPOSCHANGING also covers dwell and Windows moves.
        var change = Marshal.PtrToStructure<WindowPositionChange>(data);
        const uint NoSize = 1, NoMove = 2;
        if ((change.Flags & (NoSize | NoMove)) == (NoSize | NoMove)
            || !Native.GetWindowRect(window, out var current)) return false;
        int x = (change.Flags & NoMove) != 0 ? current.Left : change.X;
        int y = (change.Flags & NoMove) != 0 ? current.Top : change.Y;
        int width = (change.Flags & NoSize) != 0 ? current.Right - current.Left : change.Width;
        int height = (change.Flags & NoSize) != 0 ? current.Bottom - current.Top : change.Height;
        if (width <= 0 || height <= 0) return false;
        bool resizing = width != current.Right - current.Left || height != current.Bottom - current.Top;
        if (!resizing && x == current.Left && y == current.Top) return false;
        var bounds = new Native.RECT { Left = x, Top = y, Right = x + width, Bottom = y + height };
        var result = ConstrainKeyboardBounds(bounds, resizing);
        if (result.Left != x || result.Top != y)
        {
            change.X = result.Left; change.Y = result.Top;
            change.Flags &= ~NoMove;
        }
        if (result.Right - result.Left != width || result.Bottom - result.Top != height)
        {
            change.Width = result.Right - result.Left; change.Height = result.Bottom - result.Top;
            change.Flags &= ~NoSize;
        }
        Marshal.StructureToPtr(change, data, false);
        return false; // Let WPF and Windows continue processing the position change.
    }
}
