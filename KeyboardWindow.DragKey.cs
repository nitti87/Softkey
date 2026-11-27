using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

partial class KeyboardWindow
{
    DragKeyWindow dragKeyWin;
    DragKeySettingsWindow dragKeySettingsWin;
    MouseActionState mouseActions = new(Native.Click, Native.SetLeftMouseButton, Native.SetMiddleMouseButton, Native.SetRightMouseButton);
    Window draggedWindow;
    Native.POINT windowDragPointer;
    Native.RECT windowDragBounds;
    Native.POINT windowDragPosition;
    System.IntPtr windowDragHandle;
    DispatcherOperation windowDragMove;
    Native.POINT pendingWindowDragPointer;
    DispatcherTimer windowDragFallbackTimer;
    double windowDragMouseEventTime;
    bool dragKeyOrderQueued;
    WindowPosition dragKeyMoveStartPosition;

    static double DwellClock => Stopwatch.GetElapsedTime(0).TotalMilliseconds;

    void OpenDragKey()
    {
        if (desktopSessionSuspended) return;
        if (dragKeyWin != null)
        {
            if (IsDragKeyDocked) return;
            if (cfg.AlwaysOnTop) Native.KeepOnTop(new WindowInteropHelper(dragKeyWin).Handle);
            else Native.RaiseWithoutActivation(new WindowInteropHelper(dragKeyWin).Handle);
            return;
        }
        dragKeyWin = new DragKeyWindow(ToggleAutoClick, ToggleRightClick, ToggleDoubleClick, () => ToggleDrag(MouseAction.Drag),
            secureMode, ToggleKeyboardVisibility, ToggleKeyboardHeader, () => ToggleDrag(MouseAction.MiddleDrag),
            () => ToggleDrag(MouseAction.RightDrag), RequestCloseFromDragKey, MoveKeyboardToScreenEdge)
            { Topmost = cfg.AlwaysOnTop };
        dragKeyWin.MoveTitle = MoveDragKeyWithMouse;
        dragKeyWin.FloatFromBackground = FloatDragKeyFromBackground;
        dragKeyWin.MovingBounds = bounds => { if (manualDragKeyMove) PreviewDragKeyBounds(bounds); };
        dragKeyWin.LocationChanged += (_, _) => RememberWindowPosition(dragKeyWin);
        ApplyDragKeyButtons();
        RefreshDragKey();
        UpdateDragKeyPresentation();
        KeepDragKeyAboveKeyboard();
        ApplyAutoclickSettings();
    }

    void RequestCloseFromDragKey()
    {
        if (secureMode || desktopSessionSuspended || !cfg.AutoClick || closeConfirmed || closeConfirmation != null) return;
        if (!CancelMouseAction()) return;

        Window owner = IsVisible && WindowState != WindowState.Minimized ? this : dragKeyWin;
        if (UtilityDialog is { IsVisible: true, WindowState: not WindowState.Minimized } utility) owner = utility;
        if (wordListWin?.RemovalConfirmation is { IsVisible: true } removal) owner = removal;
        if (!ConfirmClose(owner) || closeConfirmed || desktopSessionSuspended || !ReleaseHeldModifiers()) return;
        closeConfirmed = true;
        Close();
    }

    void OpenDragKeySettings()
    {
        if (secureMode || desktopSessionSuspended || mainWindowActionsLocked || !CancelMouseAction() || !ReleaseHeldModifiers()) return;
        IntPtr previousForeground = Native.ForegroundWindow;
        dragKeySettingsWin = new DragKeySettingsWindow(cfg, SaveDragKeySettings) { Owner = this };
        dragKeySettingsWin.Closed += (_, _) =>
        {
            dragKeySettingsWin = null;
            SetMainWindowActionsAvailable(true);
            if (!desktopSessionSuspended)
                Dispatcher.BeginInvoke(DispatcherPriority.Background,
                    new Action(() => Native.RestoreForegroundWindow(previousForeground)));
        };
        SetMainWindowActionsAvailable(false);
        try { dragKeySettingsWin.Show(); }
        catch
        {
            dragKeySettingsWin = null;
            SetMainWindowActionsAvailable(true);
            throw;
        }
    }

    async Task<bool> SaveDragKeySettings(bool right, bool middle, bool smallIcons)
    {
        if (secureMode || desktopSessionSuspended) return false;
        var previous = (cfg.RightDragEnabled, cfg.MiddleDragEnabled, cfg.SmallDragKeyIcons);
        (cfg.RightDragEnabled, cfg.MiddleDragEnabled, cfg.SmallDragKeyIcons) = (right, middle, smallIcons);
        if (!SaveSettings())
        {
            (cfg.RightDragEnabled, cfg.MiddleDragEnabled, cfg.SmallDragKeyIcons) = previous;
            return false;
        }
        ApplyDragKeyButtons();
        if (useInstalledLayout)
        {
            try { await StartupClient.ApplyDragKeySizeAsync(smallIcons); }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException($"Sparat för skrivbordet. {ex.Message}", ex);
            }
            catch (Exception ex) when (ex is OperationCanceledException or System.IO.IOException or UnauthorizedAccessException
                or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
            {
                throw new InvalidOperationException("Sparat för skrivbordet. Dragkeys storlek vid Windows-inloggning kunde inte uppdateras.", ex);
            }
        }
        return true;
    }

    void ApplyDragKeyButtons()
    {
        if (!secureMode)
        {
            dragKeyWin?.SetDragOptions(cfg.RightDragEnabled, cfg.MiddleDragEnabled);
            dragKeyDockPreview?.SetDragOptions(cfg.RightDragEnabled, cfg.MiddleDragEnabled);
            dragKeyDockPreview?.SetSmallSize(cfg.SmallDragKeyIcons);
        }
        dragKeyWin?.SetSmallSize(cfg.SmallDragKeyIcons);
        if (IsDragKeyDocked)
            PlaceDragKeyHost(dragKeyDockHost, cfg.DragKeyPlacement, SavedDockPosition(cfg.DragKeyPlacement));
    }

    void QueueDragKeyOrder()
    {
        if (dragKeyOrderQueued || dragKeyWin?.IsVisible != true) return;
        dragKeyOrderQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new System.Action(() =>
        {
            dragKeyOrderQueued = false;
            KeepDragKeyAboveKeyboard();
        }));
    }

    void KeepDragKeyAboveKeyboard()
    {
        if (dragKeyWin?.IsVisible != true || !IsVisible || WindowState == WindowState.Minimized) return;
        var dragKey = new WindowInteropHelper(dragKeyWin).Handle;
        // Keep the mouse toolbar independent, so minimizing SoftKey does not hide it.
        // Relative priority also applies when "Always on top" is disabled.
        if (Native.IsWindowAbove(new WindowInteropHelper(this).Handle, dragKey))
            Native.RaiseWithoutActivation(dragKey);
    }

    void RefreshDragKey()
    {
        RefreshKeyboardMoveDestination();
        dragKeyWin?.Refresh(cfg.AutoClick, mouseActions.Selected,
            mouseActions.IsDragging, !mainWindowActionsLocked, draggedWindow?.Title, windowResizeEdges != WindowResizeEdges.None,
            keyboardHidden || WindowState == WindowState.Minimized, KeyboardHeaderHidden);
        if (dragKeyStatus != null)
        {
            string status = dragKeyWin?.StatusText;
            dragKeyStatus.Text = status ?? "";
            dragKeyStatus.Visibility = status == null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    bool CancelMouseAction() => CancelMouseActionCore(true);
    bool CancelMouseActionCore(bool reportFailure)
    {
        bool released = mouseActions.Cancel();
        if (released)
        {
            StopWindowDragTracking();
            draggedWindow = null;
            windowResizeEdges = WindowResizeEdges.None;
            CompleteKeyboardTextResize();
            dragKeyMoveStartPosition = null;
            ClearDragKeyDockPreview();
        }
        if (!released && reportFailure) ReportInputResult(false);
        RefreshDragKey();
        return released;
    }

    void ToggleAutoClick()
    {
        if (desktopSessionSuspended || (!cfg.AutoClick && mouseActions.IsDragging && !CancelMouseAction())) return;
        cfg.AutoClick = !cfg.AutoClick;
        ApplyAutoclickSettings();
        SaveSettings();
        SuppressCurrentDwell();
    }

    void ToggleRightClick() => SelectMouseAction(MouseAction.Right);
    void ToggleDoubleClick() => SelectMouseAction(MouseAction.Double);

    void ToggleDrag(MouseAction action)
    {
        if (secureMode || desktopSessionSuspended || !cfg.AutoClick || mainWindowActionsLocked) return;
        if (action == MouseAction.RightDrag && !cfg.RightDragEnabled
            || action == MouseAction.MiddleDrag && !cfg.MiddleDragEnabled) return;
        if (mouseActions.IsDragging)
        {
            if (mouseActions.Selected != action) return;
            FinishWindowDrag();
            SuppressCurrentDwell();
            return;
        }
        SelectMouseAction(action);
    }

    void SelectMouseAction(MouseAction action)
    {
        if (secureMode || desktopSessionSuspended || !cfg.AutoClick || mainWindowActionsLocked) return;
        mouseActions.Select(action);
        RefreshDragKey();
        SuppressCurrentDwell();
    }

    void SuppressCurrentDwell()
    {
        if (Native.GetCursorPos(out var point))
            dwellTracker.Suppress(point.X, point.Y, GetDwellTarget(point), DwellClock);
        else dwellTracker.Reset();
    }

    bool BeginWindowDrag(Window window, Native.POINT point)
    {
        if (desktopSessionSuspended || !cfg.AutoClick || mainWindowActionsLocked || window?.IsVisible != true
            || window.WindowState != WindowState.Normal
            || !Native.GetWindowRect(new WindowInteropHelper(window).Handle, out var bounds)
            || !mouseActions.BeginWindowDrag()) return false;
        CloseMenu();
        draggedWindow = window;
        if (window == dragKeyWin) dragKeyMoveStartPosition = new WindowPosition(bounds.Left, bounds.Top);
        windowResizeEdges = WindowResizeEdges.None;
        windowDragHandle = new WindowInteropHelper(window).Handle;
        windowDragPointer = point;
        windowDragBounds = bounds;
        windowDragPosition = new Native.POINT { X = bounds.Left, Y = bounds.Top };
        windowDragMouseEventTime = double.NegativeInfinity;
        if (windowDragFallbackTimer == null)
        {
            windowDragFallbackTimer = new DispatcherTimer(DispatcherPriority.Send)
                { Interval = System.TimeSpan.FromMilliseconds(16) };
            windowDragFallbackTimer.Tick += PollWindowDragPointer;
        }
        windowDragFallbackTimer.Start();
        RefreshDragKey();
        dwellTracker.Suppress(point.X, point.Y, DwellTarget.DragDrop, DwellClock);
        if (window == dragKeyWin) UpdateDragKeyDockPreview(new Point(point.X, point.Y));
        return true;
    }

    void TrackWindowDragPointer(Native.POINT point)
    {
        if (!mouseActions.IsWindowDragging) return;
        windowDragMouseEventTime = DwellClock;
        QueueWindowDragMove(point);
    }

    void PollWindowDragPointer(object sender, System.EventArgs e)
    {
        if (!mouseActions.IsWindowDragging) { StopWindowDragTracking(); return; }
        if (!Native.GetCursorPos(out var point) || Native.RightMouseButtonDown || Native.MiddleMouseButtonDown || Native.LeftMouseButtonDown)
        {
            CancelMouseAction();
            dwellTracker.Reset();
            return;
        }
        // Retain pointer tracking if Windows removes the mouse hook or it could not be installed.
        if (DwellClock - windowDragMouseEventTime >= 64) QueueWindowDragMove(point);
    }

    void QueueWindowDragMove(Native.POINT point)
    {
        if (!mouseActions.IsWindowDragging) return;
        pendingWindowDragPointer = point;
        if (windowDragMove?.Status == DispatcherOperationStatus.Pending) return;
        // Keep the global mouse hook short. Apply its latest coordinates before WPF rendering,
        // without waiting for another render frame or the dwell timer.
        windowDragMove = Dispatcher.BeginInvoke(DispatcherPriority.Send, new System.Action(() =>
        {
            windowDragMove = null;
            if (!UpdateWindowDrag(pendingWindowDragPointer)) dwellTracker.Reset();
        }));
    }

    void StopWindowDragTracking()
    {
        windowDragFallbackTimer?.Stop();
        windowDragHandle = System.IntPtr.Zero;
        if (windowDragMove?.Status == DispatcherOperationStatus.Pending) windowDragMove.Abort();
        windowDragMove = null;
    }

    bool UpdateWindowDrag(Native.POINT point)
    {
        if (!mouseActions.IsWindowDragging) return true;
        if ((secureMode && !inputDesktopActive()) || draggedWindow?.IsVisible != true || draggedWindow.WindowState != WindowState.Normal)
        {
            CancelMouseAction();
            return false;
        }
        if (windowResizeEdges != WindowResizeEdges.None) return UpdateWindowResize(point);
        // Cursor and window bounds use the same screen coordinates, including negative monitor positions.
        int x = windowDragBounds.Left + point.X - windowDragPointer.X;
        int y = windowDragBounds.Top + point.Y - windowDragPointer.Y;
        if (draggedWindow == this)
        {
            var bounded = ConstrainKeyboardBounds(new Native.RECT
            {
                Left = x, Top = y, Right = x + windowDragBounds.Right - windowDragBounds.Left,
                Bottom = y + windowDragBounds.Bottom - windowDragBounds.Top
            });
            x = bounded.Left;
            y = bounded.Top;
        }
        else if (draggedWindow == dragKeyWin)
        {
            var bounded = DragKeyScreenBounds.ConstrainToWorkArea(new Native.RECT
            {
                Left = x, Top = y, Right = x + windowDragBounds.Right - windowDragBounds.Left,
                Bottom = y + windowDragBounds.Bottom - windowDragBounds.Top
            });
            x = bounded.Left; y = bounded.Top;
        }
        if (draggedWindow == dragKeyWin) UpdateDragKeyDockPreview(new Point(point.X, point.Y));
        if (x == windowDragPosition.X && y == windowDragPosition.Y) return true;
        bool moved = Native.MoveWithoutActivation(windowDragHandle, x, y);
        if (moved) windowDragPosition = new Native.POINT { X = x, Y = y };
        if (!moved) { CancelMouseAction(); ReportInputResult(false); }
        return moved;
    }

    DwellTarget GetDwellTarget(Native.POINT point)
    {
        Point screen = new(point.X, point.Y);
        bool onDragKey = dragKeyWin != null && ((IsDragKeyDocked
            && Native.IsWindowAt(point, new WindowInteropHelper(this).Handle) && dragKeyWin.ContainsSurface(screen))
            || (dragKeyWin.IsVisible && Native.IsWindowAt(point, new WindowInteropHelper(dragKeyWin).Handle)));
        if (onDragKey && dragKeyWin.IsPowerAt(screen)) return DwellTarget.CloseSoftKey;
        // The title bar follows the pointer. Keep one target category until the drop dwell ends.
        if (mouseActions.IsWindowDragging) return DwellTarget.DragDrop;
        bool ownWindow = Native.IsSoftKeyWindowAt(point);
        if (ownWindow && cfg.AutoClick && !mainWindowActionsLocked && IsSignInMoveAreaAt(screen))
            return DwellTarget.MoveKeyboard;
        if (onDragKey)
        {
            if (cfg.AutoClick && !mainWindowActionsLocked && !mouseActions.IsDragging
                && mouseActions.IsDragSelected && dragKeyWin.IsMoveAreaAt(screen))
                return DwellTarget.MoveDragKey;
            if (cfg.AutoClick && !mainWindowActionsLocked && IsDragKeyDocked
                && mouseActions.Selected == MouseAction.Double && dragKeyWin.IsMoveAreaAt(screen))
                return DwellTarget.UndockDragKey;
            return dragKeyWin.TargetAt(screen);
        }
        if (mouseActions.IsDragging) return DwellTarget.DragDrop;
        if (!cfg.AutoClick) return DwellTarget.None;
        if (!ownWindow) return DwellTarget.External;
        if (mouseWin?.IsPointerOverSignInHelp == true) return DwellTarget.None;
        if (!mainWindowActionsLocked && mouseActions.IsDragSelected)
        {
            int hit = Native.HitTestAt(point, new WindowInteropHelper(this).Handle);
            var resize = WindowResizeGeometry.Target(WindowResizeGeometry.FromHitTest(hit));
            if (resize != DwellTarget.None) return resize;
            if (hit == 2) return DwellTarget.MoveKeyboard;
        }
        return IsPointerOnMenu(point) || IsPointInside(mouseWin, new Point(point.X, point.Y))
            || IsPointInside(passwordSuggestionAction, new Point(point.X, point.Y))
            || IsPointInside(phoneNumberKey, new Point(point.X, point.Y))
            || (ShowingEmailSuggestions && IsPointInside(suggestionViewport, new Point(point.X, point.Y)))
            || mainWindowActionsLocked ? DwellTarget.SoftKeyControl : DwellTarget.SoftKeyRepeat;
    }
}
