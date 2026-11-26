using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;

// Native visibility preserves WPF modal loops, window positions and minimized state.
// Hiding a modal dialog with Window.Hide() would end its ShowDialog call.
sealed class DesktopWindowVisibility
{
    readonly Dictionary<Window, IntPtr> hidden = new();
    public void Suspend(IEnumerable<Window> windows)
    {
        var visible = new List<IntPtr>();
        foreach (var window in windows)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero || !Native.WindowIsVisible(handle)) continue;
            hidden.TryAdd(window, handle);
            visible.Add(handle);
        }
        // Capture owned dialogs before hiding their owner changes native visibility.
        foreach (IntPtr handle in visible) Native.SetWindowVisibility(handle, false);
    }
    public void Resume()
    {
        foreach (var entry in hidden)
        {
            if (entry.Key.Visibility == Visibility.Visible && new WindowInteropHelper(entry.Key).Handle == entry.Value)
                Native.SetWindowVisibility(entry.Value, true);
        }
        hidden.Clear();
    }
    public void Clear() => hidden.Clear();
}
