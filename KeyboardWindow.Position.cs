using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

partial class KeyboardWindow
{
    readonly DispatcherTimer positionSaveTimer = new(DispatcherPriority.Background)
        { Interval = TimeSpan.FromMilliseconds(400) };
    bool positionTrackingReady, nativeWindowMove;

    void InitializePositionTracking()
    {
        LocationChanged += (_, _) =>
        {
            RememberWindowPosition(this);
            RefreshKeyboardMoveDestination();
        };
        positionSaveTimer.Tick += (_, _) =>
        {
            // Save after the move, never flush files while the pointer is moving a window.
            if (mouseActions.IsWindowDragging || manualDragKeyMove || nativeWindowMove) return;
            positionSaveTimer.Stop();
            SaveSettings();
        };
    }

    void RestoreKeyboardPosition()
    {
        if (!WindowPositioning.Restore(new WindowInteropHelper(this).Handle, cfg.KeyboardPosition))
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - ActualWidth) / 2;
            Top = area.Bottom - ActualHeight;
        }
        ApplyKeyboardEdges();
    }

    void RememberWindowPosition(Window window)
    {
        if (desktopSessionSuspended || !positionTrackingReady || changingKeyboardHeader
            || window?.IsVisible != true || window.WindowState != WindowState.Normal) return;
        if (window == dragKeyWin && (cfg.DragKeyPlacement != DragKeyPlacement.Floating || !hasFloatingDragKeyPosition)) return;
        var position = WindowPositioning.Capture(new WindowInteropHelper(window).Handle);
        if (position == null) return;
        if (window == this && KeyboardHeaderHidden
            && Native.GetWindowRect(new WindowInteropHelper(this).Handle, out var compact))
        {
            double scale = PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice.M22;
            int change = (int)Math.Round(hiddenHeaderHeight * scale);
            position = new WindowPosition(position.X, AdjustKeyboardHeight(compact, change).Top);
        }
        var previous = window == this ? cfg.KeyboardPosition : cfg.DragKeyPosition;
        if (position == previous) return;
        if (window == this) cfg.KeyboardPosition = position;
        else cfg.DragKeyPosition = position;
        positionSaveTimer.Stop();
        positionSaveTimer.Start();
    }

    void CaptureWindowPositions()
    {
        RememberWindowPosition(this);
        RememberWindowPosition(dragKeyWin);
        positionSaveTimer.Stop();
    }
}
