using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

partial class KeyboardWindow
{
    const double CompactBottomInset = 3;
    // A temporary display choice, independent of minimizing and saved docking preferences.
    bool keyboardHidden;
    double hiddenHeaderHeight; // Store the normal frame size even while the header is hidden.
    bool changingKeyboardHeader;
    Native.RECT compactFrame;
    int compactBottomInsetPixels;

    bool KeyboardHeaderHidden => hiddenHeaderHeight > 0;
    double NormalKeyboardHeight => ActualHeight + hiddenHeaderHeight;
    double RemovedCompactBottomPadding => (compactFrame.Bottom - compactBottomInsetPixels) / VisualTreeHelper.GetDpi(this).DpiScaleY;

    void ToggleKeyboardVisibility()
    {
        if (secureMode || desktopSessionSuspended || mainWindowActionsLocked || !CancelMouseAction()) return;
        if (!keyboardHidden && WindowState != WindowState.Minimized)
        {
            if (!ReleaseHeldModifiers()) return;
            CaptureWindowPositions();
            CloseMenu();
            ClearFocusedSuggestions();
            keyboardHidden = true;
            // Detach a docked toolbar before hiding its host, keeping the way back accessible.
            UpdateDragKeyPresentation();
            Hide();
        }
        else
        {
            keyboardHidden = false;
            ShowActivated = false;
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Show();
            UpdateDragKeyPresentation();
            Native.RaiseWithoutActivation(new WindowInteropHelper(this).Handle);
            KeepDragKeyAboveKeyboard();
        }
        RefreshDragKey();
        SuppressCurrentDwell();
    }

    void ToggleKeyboardHeader()
    {
        if (secureMode || desktopSessionSuspended || mainWindowActionsLocked
            || !cfg.AutoClick || mouseActions.IsDragging || !CancelMouseAction()) return;

        bool wasHidden = KeyboardHeaderHidden;
        if (!TrySetDesktopHeader(!wasHidden, cfg.MenuAboveDragKey))
        {
            ReportInputResult(false);
            return;
        }
        bool previous = cfg.HideKeyboardTitleBar;
        cfg.HideKeyboardTitleBar = KeyboardHeaderHidden;
        if (SaveSettings()) return;
        cfg.HideKeyboardTitleBar = previous;
        TrySetDesktopHeader(wasHidden, cfg.MenuAboveDragKey);
    }

    bool TrySetDesktopHeader(bool hide, bool menuAbove)
    {
        bool wasHidden = KeyboardHeaderHidden;
        bool wasAbove = cfg.MenuAboveDragKey;
        if (hide == wasHidden && menuAbove == wasAbove) return true;
        if (secureMode || changingKeyboardHeader || !IsVisible || WindowState != WindowState.Normal
            || PresentationSource.FromVisual(this) is not HwndSource source) return false;
        var handle = source.Handle;
        if (!TryGetFrameHeight(handle, out var before, out int frameHeight)) return false;

        CloseMenu();
        double scale = source.CompositionTarget.TransformToDevice.M22;
        double previousRowsHeight = DesktopHeaderRowsHeight;
        double previousReduction = hiddenHeaderHeight;
        double previousMinimum = MinHeight;
        bool titleChanged = hide != wasHidden;
        bool resized = false;
        changingKeyboardHeader = true;
        try
        {
            cfg.MenuAboveDragKey = menuAbove;
            if (titleChanged) SetTitleBarVisibility(!hide);
            ApplyDesktopMenuLayout(!hide);
            UpdateLayout();
            if (!TryGetFrameHeight(handle, out _, out int newFrameHeight)
                || (titleChanged && hide && newFrameHeight >= frameHeight))
                return false;
            int captionChange = newFrameHeight - frameHeight;
            double captionReduction = titleChanged ? -captionChange / scale
                : previousReduction - (wasAbove ? DesktopMenuRowHeight : 0);
            hiddenHeaderHeight = hide ? captionReduction + (menuAbove ? DesktopMenuRowHeight : 0) : 0;
            UpdateKeyboardMinimumSize();

            int heightChange = captionChange + (int)Math.Round((DesktopHeaderRowsHeight - previousRowsHeight) * scale);
            var bounds = AdjustKeyboardHeight(before, heightChange);
            resized = Native.ResizeWithoutActivation(handle, bounds.Left, bounds.Top,
                bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        }
        finally
        {
            if (!resized)
            {
                cfg.MenuAboveDragKey = wasAbove;
                if (titleChanged) SetTitleBarVisibility(!wasHidden);
                ApplyDesktopMenuLayout(!wasHidden);
                hiddenHeaderHeight = previousReduction;
                MinHeight = previousMinimum;
            }
            UpdateLayout();
            changingKeyboardHeader = false;
            RememberKeyboardSize();
            RememberWindowPosition(this);
            RefreshDragKey();
            SuppressCurrentDwell();
        }
        return resized;
    }

    void SetTitleBarVisibility(bool visible)
    {
        WindowStyle = visible ? WindowStyle.SingleBorderWindow : WindowStyle.None;
        if (visible && Content is FrameworkElement surface)
            surface.Margin = new Thickness(KeyboardInset, 0, KeyboardInset, 0);
    }

    bool UpdateTitlelessFrame(IntPtr window)
    {
        if (!Native.TryGetResizeFrame(window, out compactFrame, out uint dpi)) return false;
        double scale = dpi / 96.0;
        // The status row needs less visual padding than the native resize target below it.
        compactBottomInsetPixels = Math.Min(compactFrame.Bottom,
            (int)Math.Round(CompactBottomInset * scale, MidpointRounding.AwayFromZero));
        if (Content is FrameworkElement surface)
        {
            surface.Margin = new Thickness(KeyboardInset - compactFrame.Left / scale, 0,
                KeyboardInset + compactFrame.Right / scale, compactBottomInsetPixels / scale);
        }
        return true;
    }

    bool TryGetFrameHeight(IntPtr handle, out Native.RECT bounds, out int height)
    {
        height = 0;
        if (!Native.GetWindowRect(handle, out bounds) || !Native.GetClientRect(handle, out var client)) return false;
        height = bounds.Bottom - bounds.Top - (client.Bottom - client.Top);
        if (WindowStyle == WindowStyle.None) height += compactBottomInsetPixels;
        return true;
    }

    static Native.RECT AdjustKeyboardHeight(Native.RECT bounds, int change)
    {
        int height = Math.Max(1, bounds.Bottom - bounds.Top + change);
        if (WindowPositioning.TryWorkArea(bounds, out var area))
        {
            height = Math.Min(height, area.Bottom - area.Top);
            // At the upper snap position resize down from the edge. Elsewhere keep
            // the bottom edge fixed so the keys stay where the user left them.
            if (bounds.Top == area.Top)
            {
                bounds.Bottom = bounds.Top + height;
                return bounds;
            }
            bounds.Bottom = Math.Clamp(bounds.Bottom, area.Top + height, area.Bottom);
        }
        bounds.Top = bounds.Bottom - height;
        return bounds;
    }
}
