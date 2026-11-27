using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

partial class KeyboardWindow
{
    const double SignInMoveGripHeight = 20;
    Border signInMoveGrip;
    KeyboardAreaSize? appliedSignInKeyboardArea;

    UIElement BuildSignInMoveGrip()
    {
        signInMoveGrip = new Border
        {
            Height = SignInMoveGripHeight, Background = WindowSurface, Cursor = Cursors.SizeAll,
            Child = new Border
            {
                Width = 32, Height = 3, CornerRadius = new CornerRadius(1.5),
                Background = SoftKeyKeyDesign.Shadow,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            }
        };
        System.Windows.Automation.AutomationProperties.SetName(signInMoveGrip, "Flytta tangentbordet");
        System.Windows.Automation.AutomationProperties.SetHelpText(signInMoveGrip,
            "Vila här för att flytta tangentbordet. Flytta pekaren och vila igen för att släppa.");
        signInMoveGrip.MouseLeftButtonDown += (_, e) =>
        {
            if (!inputDesktopActive() || !CancelMouseAction()) return;
            e.Handled = true;
            try { DragMove(); }
            finally { SuppressCurrentDwell(); }
        };
        return signInMoveGrip;
    }

    void ApplySignInKeyboardArea(KeyboardAreaSize? size)
    {
        if (!secureMode || size is not { IsValid: true } target || appliedSignInKeyboardArea == target) return;
        if (FitSignInKeyboardArea(target)) appliedSignInKeyboardArea = target;
    }

    void ApplySignInDragKeySize(bool small, KeyboardAreaSize? savedArea)
    {
        if (!secureMode || cfg.SmallDragKeyIcons == small) return;
        var currentArea = CurrentKeyboardArea();
        cfg.SmallDragKeyIcons = small;
        UpdateKeyboardMinimumSize();
        ApplyDragKeyButtons();
        // Resizing DragKey should not undo a manual resize of the keys in this session.
        if ((savedArea == null || savedArea == appliedSignInKeyboardArea) && currentArea is { } area)
            FitSignInKeyboardArea(area);
        SuppressCurrentDwell();
    }

    bool FitSignInKeyboardArea(KeyboardAreaSize target)
    {
        UpdateLayout();
        if (keyboardKeyGrid.ActualWidth <= 0 || keyboardKeyGrid.ActualHeight <= 0) return false;
        var handle = new WindowInteropHelper(this).Handle;
        if (!Native.GetWindowRect(handle, out var current) || !WindowPositioning.TryWorkArea(current, out var area)) return false;

        var dpi = VisualTreeHelper.GetDpi(this);
        int oldWidth = current.Right - current.Left, oldHeight = current.Bottom - current.Top;
        int availableWidth = area.Right - area.Left, availableHeight = area.Bottom - area.Top;
        double frameWidth = Math.Max(0, oldWidth - keyboardKeyGrid.ActualWidth * dpi.DpiScaleX);
        double frameHeight = Math.Max(0, oldHeight - keyboardKeyGrid.ActualHeight * dpi.DpiScaleY);
        double keyWidth = target.Width * dpi.DpiScaleX, keyHeight = target.Height * dpi.DpiScaleY;
        double fit = Math.Clamp(Math.Min((availableWidth - frameWidth) / keyWidth, (availableHeight - frameHeight) / keyHeight), 0, 1);
        int width = Math.Min(availableWidth, Math.Max((int)Math.Ceiling(MinWidth * dpi.DpiScaleX), (int)Math.Round(frameWidth + keyWidth * fit)));
        int height = Math.Min(availableHeight, Math.Max((int)Math.Ceiling(MinHeight * dpi.DpiScaleY), (int)Math.Round(frameHeight + keyHeight * fit)));
        int left = Math.Clamp(current.Left + (oldWidth - width) / 2, area.Left, area.Right - width);
        int top = current.Top == area.Top ? area.Top : Math.Clamp(current.Bottom - height, area.Top, area.Bottom - height);

        // Keep AutoClick's current scale while fitting the key area around it.
        var previousReference = dragKeyReferenceSize;
        dragKeyReferenceSize = new Size(previousReference.Width * width / oldWidth, previousReference.Height * height / oldHeight);
        if (!Native.ResizeWithoutActivation(handle, left, top, width, height))
        {
            dragKeyReferenceSize = previousReference;
            return false;
        }
        UpdateLayout();
        return true;
    }

    bool IsSignInMoveAreaAt(Point screen)
    {
        if (!secureMode || signInMoveGrip?.IsVisible != true || PresentationSource.FromVisual(signInMoveGrip) == null) return false;
        Point local = signInMoveGrip.PointFromScreen(screen);
        return local.X >= 0 && local.Y >= 0 && local.X < signInMoveGrip.ActualWidth && local.Y < signInMoveGrip.ActualHeight;
    }

    bool BeginSignInWindowDrag(Native.POINT point)
    {
        if (!secureMode || !inputDesktopActive() || !cfg.AutoClick || mainWindowActionsLocked || !CancelMouseAction()) return false;
        // Reuse the existing own-window move. No Windows mouse button is held or injected.
        mouseActions.Select(MouseAction.Drag);
        if (BeginWindowDrag(this, point)) return true;
        CancelMouseAction();
        return false;
    }
}
