using System;
using System.Windows;
using System.Windows.Interop;

partial class KeyboardWindow
{
    WindowResizeEdges windowResizeEdges;
    Size windowResizeMinimum;
    Rect windowResizePosition;
    Size dragKeyReferenceSize;
    double dragKeyScale = 1;
    bool keyboardTextResizePending;

    void UpdateKeyboardTextForSize()
    {
        // Key legends still fit to the available space. Reapply their shared base
        // size after the gesture to avoid a second layout pass on every step.
        keyboardTextResizePending = nativeWindowMove || windowResizeEdges != WindowResizeEdges.None;
        if (!keyboardTextResizePending) ScaleText(keyboardKeyGrid);
    }

    void CompleteKeyboardTextResize()
    {
        if (keyboardTextResizePending) UpdateKeyboardTextForSize();
    }

    bool BeginWindowResize(WindowResizeEdges edges, Native.POINT point)
    {
        if (WindowResizeGeometry.Target(edges) == DwellTarget.None || !BeginWindowDrag(this, point)) return false;
        windowResizeEdges = edges;
        var transform = PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice;
        Vector minimum = transform.Transform(new Vector(MinWidth, MinHeight));
        windowResizeMinimum = new Size(Math.Ceiling(minimum.X), Math.Ceiling(minimum.Y));
        windowResizePosition = new Rect(windowDragBounds.Left, windowDragBounds.Top,
            windowDragBounds.Right - windowDragBounds.Left, windowDragBounds.Bottom - windowDragBounds.Top);
        RefreshDragKey();
        return true;
    }

    bool UpdateWindowResize(Native.POINT point)
    {
        var original = new Rect(windowDragBounds.Left, windowDragBounds.Top,
            windowDragBounds.Right - windowDragBounds.Left, windowDragBounds.Bottom - windowDragBounds.Top);
        Rect bounds = WindowResizeGeometry.Resize(original, windowResizeEdges,
            new Vector(point.X - windowDragPointer.X, point.Y - windowDragPointer.Y), windowResizeMinimum);
        if (bounds == windowResizePosition) return true;
        bool changed = Native.ResizeWithoutActivation(windowDragHandle, (int)bounds.X, (int)bounds.Y,
            (int)bounds.Width, (int)bounds.Height);
        if (changed) windowResizePosition = bounds;
        else { CancelMouseAction(); ReportInputResult(false); }
        return changed;
    }

    void RememberKeyboardSize()
    {
        if (WindowState != WindowState.Normal || changingDragKeyDock || changingKeyboardHeader) return;
        RefreshDragKeyScale();
        cfg.WindowWidth = ActualWidth - DockWidth(cfg.DragKeyPlacement);
        cfg.WindowHeight = NormalKeyboardHeight - DockHeight(cfg.DragKeyPlacement);
    }

    void RefreshDragKeyScale()
    {
        double scale = WindowResizeGeometry.PanelScale(new Size(ActualWidth, NormalKeyboardHeight), dragKeyReferenceSize);
        if (Math.Abs(dragKeyScale - scale) < 0.00001) return;
        dragKeyScale = scale;
        dragKeyWin?.SetDockScale(scale);
        if (IsDragKeyDocked) PlaceDragKeyHost(dragKeyDockHost, cfg.DragKeyPlacement, SavedDockPosition(cfg.DragKeyPlacement));
        if (dragKeyDockPreview?.IsVisible == true)
        {
            dragKeyDockPreview.SetDockScale(ToolbarDockScale);
            PlaceDragKeyHost(dragKeyDockPreview, dragKeyPreviewPlacement, dragKeyPreviewPosition);
        }
    }
}
