using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

// Autoklick (dwell-klick) och Jitter Box.
// The same timer also watches the resume button while AutoClick is off.
partial class KeyboardWindow
{
    const uint EventSystemForeground = 0x0003;
    const uint EventObjectShow = 0x8002;
    const uint WinEventOutOfContext = 0x0000;
    const uint WinEventSkipOwnProcess = 0x0002;

    readonly DwellTracker dwellTracker = new();
    DesktopDwellTargets desktopDwellTargets;
    IntPtr foregroundEventHook, windowShowEventHook;
    WinEventProc topmostEventProc;
    bool topmostReassertQueued;

    delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId,
        int childId, uint eventThread, uint eventTime);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module,
        WinEventProc callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);

    void StartTopmostEnforcement()
    {
        if (desktopSessionSuspended || !cfg.AlwaysOnTop) return;
        topmostTimer.Start();
        if (!IsKeyboardLayoutSaving) Native.KeepOnTop(new WindowInteropHelper(this).Handle);

        topmostEventProc ??= OnTopmostWindowEvent;
        const uint hookFlags = WinEventOutOfContext | WinEventSkipOwnProcess;
        if (foregroundEventHook == IntPtr.Zero)
            foregroundEventHook = SetWinEventHook(EventSystemForeground, EventSystemForeground,
                IntPtr.Zero, topmostEventProc, 0, 0, hookFlags);
        if (windowShowEventHook == IntPtr.Zero)
            windowShowEventHook = SetWinEventHook(EventObjectShow, EventObjectShow,
                IntPtr.Zero, topmostEventProc, 0, 0, hookFlags);
    }

    void StopTopmostEnforcement()
    {
        topmostTimer.Stop();
        if (foregroundEventHook != IntPtr.Zero) UnhookWinEvent(foregroundEventHook);
        if (windowShowEventHook != IntPtr.Zero) UnhookWinEvent(windowShowEventHook);
        foregroundEventHook = windowShowEventHook = IntPtr.Zero;
    }

    void OnTopmostWindowEvent(IntPtr hook, uint eventType, IntPtr window, int objectId,
        int childId, uint eventThread, uint eventTime)
    {
        try
        {
            if (desktopSessionSuspended || !cfg.AlwaysOnTop || window == IntPtr.Zero || Dispatcher.HasShutdownStarted) return;
            if (eventType == EventObjectShow && (objectId != 0 || childId != 0 || !Native.IsTopmost(window))) return;
            if (topmostReassertQueued) return;

            topmostReassertQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                topmostReassertQueued = false;
                if (cfg.AlwaysOnTop && IsLoaded) KeepSoftKeyOnTop();
            }));
        }
        catch (Exception ex)
        {
            topmostReassertQueued = false;
            System.Diagnostics.Trace.WriteLine($"Window callback failed: {ex.GetType().Name}");
        }
    }

    void ApplyAutoclickSettings()
    {
        if (desktopSessionSuspended) { dwellTimer.Stop(); return; }
        dwellTracker.Reset();
        CancelMouseAction();
        mouseWin?.RefreshAutoClickState();
        UpdateDwellTimer();
    }

    static async Task SaveSignInAutoclickTimingAsync(AutoclickTimingSettings timing)
    {
        try { await StartupClient.ApplyAutoclickTimingAsync(timing); }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException($"Sparat för skrivbordet. {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is OperationCanceledException or System.IO.IOException
            or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
        {
            throw new InvalidOperationException("Sparat för skrivbordet. Windows-inloggningen kunde inte uppdateras. Försök igen.", ex);
        }
    }

    void UpdateDwellTimer()
    {
        if (!desktopSessionSuspended && (cfg.AutoClick || mouseActions.HeldDragButton.HasValue
            || dragKeyWin?.IsVisible == true || IsDragKeyDocked || mouseWin?.IsVisible == true))
            dwellTimer.Start();
        else dwellTimer.Stop();
    }

    void Dwell(object s, EventArgs e)
    {
        if (desktopSessionSuspended || (secureMode && !inputDesktopActive()))
        {
            mouseWin?.ResetDwellPreview();
            desktopDwellTargets?.Leave();
            dwellTracker.Reset();
            return;
        }
        if (secureMode) RefreshKeyboardLabels();
        if (!cfg.AutoClick && mouseActions.HeldDragButton.HasValue && CancelMouseActionCore(false))
            UpdateDwellTimer();
        var heldButton = mouseActions.HeldDragButton;
        if (!Native.GetCursorPos(out var p) || (heldButton != MouseAction.RightDrag && Native.RightMouseButtonDown)
            || (heldButton != MouseAction.Drag && Native.LeftMouseButtonDown)
            || (heldButton != MouseAction.MiddleDrag && Native.MiddleMouseButtonDown))
        {
            if (mouseActions.IsWindowDragging) CancelMouseAction();
            mouseWin?.ResetDwellPreview();
            desktopDwellTargets?.Leave();
            dwellTracker.Reset();
            return;
        }
        if (mouseWin?.UpdateDwellPreview(p, DwellClock) == true)
        {
            desktopDwellTargets?.Leave();
            dwellTracker.Reset();
            return;
        }
        DwellTarget target = GetDwellTarget(p);
        if (!secureMode && target == DwellTarget.External)
        {
            // A completed single click cannot fire again at this position, even if the control changes.
            if (!cfg.Repeat && mouseActions.Selected == MouseAction.Left && dwellTracker.IsBlockedAt(p.X, p.Y)) return;
            desktopDwellTargets ??= new DesktopDwellTargets(Dispatcher);
            desktopDwellTargets.Observe(p, target, DwellClock, dwellTracker.VisitVersion, TrackExternalDwell);
            return;
        }
        desktopDwellTargets?.Leave();
        object control = target is DwellTarget.SoftKeyControl or DwellTarget.SoftKeyRepeat
            ? DesktopDwellTargets.FindLocal(p, wordMenuAnchor) : null;
        TrackDwell(p, target, control);
    }

    void TrackExternalDwell(DesktopDwellTargets.Sample sample, DwellTarget target, int visitVersion)
    {
        if (!IsLoaded || desktopSessionSuspended || !cfg.AutoClick || visitVersion != dwellTracker.VisitVersion
            || !Native.GetCursorPos(out var point) || !sample.Contains(point) || !Native.IsWindowAt(point, sample.Window)
            || Native.LeftMouseButtonDown || Native.RightMouseButtonDown || Native.MiddleMouseButtonDown
            || GetDwellTarget(point) != target) return;
        TrackDwell(point, target, sample.Identity);
    }

    void TrackDwell(Native.POINT point, DwellTarget target, object control)
    {
        if (dwellTracker.Update(point.X, point.Y, target, DwellClock, cfg.DwellMs, cfg.JitterPx, cfg.Repeat, control))
            PerformDwell(target);
    }

    void PerformDwell(DwellTarget target)
    {
        if (desktopSessionSuspended || (secureMode && !inputDesktopActive())) { dwellTracker.Reset(); return; }
        if (target == DwellTarget.None || (!cfg.AutoClick && target != DwellTarget.AutoClick)) return;
        bool special = target == DwellTarget.DragDrop
            || WindowResizeGeometry.Edges(target) != WindowResizeEdges.None
            || target is DwellTarget.MoveKeyboard or DwellTarget.MoveDragKey
            || (target == DwellTarget.External && mouseActions.Selected != MouseAction.Left);
        bool singleAction = target is DwellTarget.AutoClick or DwellTarget.RightClick
            or DwellTarget.DoubleClick or DwellTarget.Drag or DwellTarget.MiddleDrag or DwellTarget.RightDrag or DwellTarget.UndockDragKey
            or DwellTarget.ToggleKeyboard or DwellTarget.ToggleKeyboardHeader or DwellTarget.CloseSoftKey
            or DwellTarget.MoveKeyboardEdge or DwellTarget.SoftKeyControl;
        dwellTracker.Complete(DwellClock, allowRepeat: cfg.Repeat && !singleAction && !special);
        if (target == DwellTarget.AutoClick) ToggleAutoClick();
        else if (target == DwellTarget.RightClick) ToggleRightClick();
        else if (target == DwellTarget.DoubleClick) ToggleDoubleClick();
        else if (target == DwellTarget.Drag) ToggleDrag(MouseAction.Drag);
        else if (target == DwellTarget.RightDrag) ToggleDrag(MouseAction.RightDrag);
        else if (target == DwellTarget.MiddleDrag) ToggleDrag(MouseAction.MiddleDrag);
        else if (target == DwellTarget.ToggleKeyboard) ToggleKeyboardVisibility();
        else if (target == DwellTarget.ToggleKeyboardHeader) ToggleKeyboardHeader();
        else if (target == DwellTarget.MoveKeyboardEdge) MoveKeyboardToScreenEdge();
        else if (target == DwellTarget.CloseSoftKey)
        {
            // Open the modal prompt after the dwell timer has scheduled its next tick.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(RequestCloseFromDragKey));
        }
        else if (WindowResizeGeometry.Edges(target) != WindowResizeEdges.None)
        {
            if (Native.GetCursorPos(out var point)) ReportInputResult(BeginWindowResize(WindowResizeGeometry.Edges(target), point));
        }
        else if (target == DwellTarget.UndockDragKey)
        {
            if (Native.GetCursorPos(out var point)) FloatDragKeyFromBackground(new Point(point.X, point.Y));
        }
        else if (target is DwellTarget.MoveKeyboard or DwellTarget.MoveDragKey)
        {
            if (Native.GetCursorPos(out var point))
                ReportInputResult(target == DwellTarget.MoveKeyboard
                    ? secureMode ? BeginSignInWindowDrag(point) : BeginWindowDrag(this, point)
                    : BeginDragKeyMove(point));
        }
        else if (target is DwellTarget.External or DwellTarget.DragDrop)
        {
            ReportInputResult(mouseActions.IsWindowDragging ? FinishWindowDrag() : mouseActions.Perform());
            RefreshDragKey();
            // Drag start/drop changes the target category. Require movement before the next dwell.
            if (special) SuppressCurrentDwell();
        }
        else ReportInputResult(Native.Click(0));
    }

    void KeepSoftKeyOnTop()
    {
        if (desktopSessionSuspended || !cfg.AlwaysOnTop) return;
        bool keyboardVisible = IsVisible && WindowState != WindowState.Minimized;
        var keyboard = keyboardVisible ? new WindowInteropHelper(this).Handle : IntPtr.Zero;
        // During a save, keep the keyboard underneath its own cover and the dialogs.
        if (keyboardVisible && !IsKeyboardLayoutSaving && Native.NeedsTopmostRaise(keyboard))
            Native.KeepOnTop(keyboard);

        // Raise the keyboard first, then the mouse toolbar so SoftKey cannot cover DragKey.
        var belowMenu = keyboard;
        if (dragKeyWin?.IsVisible == true)
        {
            var dragKey = new WindowInteropHelper(dragKeyWin).Handle;
            if (Native.NeedsTopmostRaise(dragKey, keyboard)) Native.KeepOnTop(dragKey);
            belowMenu = dragKey;
        }
        if (!keyboardVisible || passwordVerification != null) return;

        Window dialog = UtilityDialog;
        if (dialog?.IsVisible == true)
        {
            var dialogHandle = new WindowInteropHelper(dialog).Handle;
            if (Native.NeedsTopmostRaise(dialogHandle, belowMenu)) Native.KeepOnTop(dialogHandle);
            belowMenu = dialogHandle;
        }

        if (wordListWin?.RemovalConfirmation?.IsVisible == true)
        {
            var confirmation = new WindowInteropHelper(wordListWin.RemovalConfirmation).Handle;
            if (Native.NeedsTopmostRaise(confirmation, belowMenu)) Native.KeepOnTop(confirmation);
            belowMenu = confirmation;
        }
        if (closeConfirmation?.IsVisible == true)
        {
            var confirmation = new WindowInteropHelper(closeConfirmation).Handle;
            if (Native.NeedsTopmostRaise(confirmation, belowMenu)) Native.KeepOnTop(confirmation);
            belowMenu = confirmation;
        }

        // Menyn har ett eget fönster. Låt den ligga still så länge den redan ligger rätt.
        if (menuPopup?.IsOpen == true && menuPanel != null
            && PresentationSource.FromVisual(menuPanel) is HwndSource menuSource
            && Native.NeedsTopmostRaise(menuSource.Handle, belowMenu))
            Native.KeepOnTop(menuSource.Handle);
    }

    internal void RecoverFromTransientError()
    {
        desktopDwellTargets?.Leave();
        dwellTracker.Reset();
        mouseWin?.ResetDwellPreview();
        if (mouseActions.IsWindowDragging) CancelMouseAction();
        if (openMenu >= 0) CloseMenu();
        checkingDisplayedWord = false;
        emailInsertion = null;
    }
}
