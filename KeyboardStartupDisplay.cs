using System;
using System.Windows;
using System.Windows.Interop;

// Observe from the existing session monitor, after WPF has rendered on the right desktop.
sealed class KeyboardStartupDisplay
{
    readonly Window window;
    readonly Action shown;
    bool rendered, reported;

    public KeyboardStartupDisplay(Window window, Action shown)
    {
        this.window = window;
        this.shown = shown;
        window.ContentRendered += (_, _) => rendered = true;
    }

    public void Observe(bool active)
    {
        if (reported || !rendered || !active || !window.IsVisible || window.WindowState == WindowState.Minimized
            || !Native.WindowIsVisible(new WindowInteropHelper(window).Handle)) return;
        reported = true;
        shown();
    }
}
