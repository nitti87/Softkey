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
    bool inputRecoveryPending;
    const string InputRecoveryStatus = "SoftKey återställer inmatningen…";
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
        if (!desktopSessionSuspended && (inputRecoveryPending || cfg.AutoClick || mouseActions.HeldDragButton.HasValue
            || dragKeyWin?.IsVisible == true || IsDragKeyDocked || mouseWin?.IsVisible == true))
            dwellTimer.Start();
        else dwellTimer.Stop();
    }

    void Dwell(object s, EventArgs e)
    {
        if (!DwellDesktopActive())
        {
            mouseWin?.ResetDwellPreview();
            desktopDwellTargets?.Leave();
            dwellTracker.Reset();
            return;
        }
        if (inputRecoveryPending)
        {
            ReleaseRecoveredInput();
            if (!inputRecoveryPending)
            {
                SuppressCurrentDwell();
                UpdateDwellTimer();
            }
            return;
        }
        if (secureMode) RefreshKeyboardLabels();
        if (!cfg.AutoClick && mouseActions.HeldDragButton.HasValue && CancelMouseActionCore(false))
            UpdateDwellTimer();
        if (!TryReadDwellPointer(out var p))
        {
            if (mouseActions.IsWindowDragging) CancelMouseAction();
            mouseWin?.ResetDwellPreview();
            desktopDwellTargets?.Leave();
            dwellTracker.Reset();
            return;
        }
        double now = DwellClock;
        bool canStartDwell = dwellTracker.ObserveMovement(p.X, p.Y, cfg.JitterPx, now);
        if (mouseWin?.UpdateDwellPreview(p, now) == true)
        {
            desktopDwellTargets?.Leave();
            dwellTracker.Reset();
            return;
        }
        if (!canStartDwell) return;
        DwellTarget target = GetDwellTarget(p);
        if (!secureMode && target == DwellTarget.External)
        {
            desktopDwellTargets ??= new DesktopDwellTargets(Dispatcher);
            desktopDwellTargets.Observe(p, target, now, dwellTracker.VisitVersion, TrackExternalDwell);
            return;
        }
        desktopDwellTargets?.Leave();
        TrackDwell(p, target, now);
    }

    void TrackExternalDwell(DesktopDwellTargets.Sample sample, DwellTarget target, int visitVersion)
    {
        if (!IsLoaded || !DwellDesktopActive() || visitVersion != dwellTracker.VisitVersion || !TryReadDwellPointer(out var point)
            || !sample.Contains(point) || !Native.IsWindowAt(point, sample.Window) || GetDwellTarget(point) != target) return;
        TrackDwell(point, target, DwellClock, sample);
    }

    bool DwellDesktopActive() => !desktopSessionSuspended && (!secureMode || inputDesktopActive());

    bool IsDwellTargetEnabled(DwellTarget target)
        => !inputRecoveryPending && target != DwellTarget.None && (cfg.AutoClick || target == DwellTarget.AutoClick);

    bool CanRepeatDwell(DwellTarget target)
        => cfg.Repeat && (target == DwellTarget.SoftKeyRepeat
            || (target == DwellTarget.External && mouseActions.Selected == MouseAction.Left));

    bool TryReadDwellPointer(out Native.POINT point)
    {
        var heldButton = mouseActions.HeldDragButton;
        return Native.GetCursorPos(out point)
            && (heldButton == MouseAction.Drag || !Native.LeftMouseButtonDown)
            && (heldButton == MouseAction.RightDrag || !Native.RightMouseButtonDown)
            && (heldButton == MouseAction.MiddleDrag || !Native.MiddleMouseButtonDown);
    }

    void TrackDwell(Native.POINT point, DwellTarget target, double now, DesktopDwellTargets.Sample? external = null)
    {
        if (!IsDwellTargetEnabled(target)) { dwellTracker.Reset(); return; }
        var window = external?.Window ?? Native.WindowFromPoint(point);
        if (window == IntPtr.Zero) { dwellTracker.Reset(); return; }
        bool localControl = target is DwellTarget.SoftKeyControl or DwellTarget.SoftKeyRepeat;
        object control = localControl ? DesktopDwellTargets.FindLocal(point, wordMenuAnchor) : external?.Identity;
        bool repeat = CanRepeatDwell(target);
        if (!dwellTracker.Update(point.X, point.Y, target, now, cfg.DwellMs, cfg.JitterPx, repeat, control)) return;

        // Recheck the live pointer and target before dispatching the click. A cached UIA sample
        // may only be used in its window and bounds, with no blocking UIA call on the UI thread.
        if (!TryReadDwellPointer(out var current) || !Native.IsWindowAt(current, window)
            || GetDwellTarget(current) != target || (external.HasValue && !external.Value.Contains(current))
            || (localControl && !Equals(control, DesktopDwellTargets.FindLocal(current, wordMenuAnchor))))
        {
            dwellTracker.Reset();
            return;
        }
        if (dwellTracker.Update(current.X, current.Y, target, DwellClock, cfg.DwellMs, cfg.JitterPx, repeat, control))
            PerformDwell(target);
    }

    void PerformDwell(DwellTarget target)
    {
        if (!DwellDesktopActive() || !IsDwellTargetEnabled(target)) { dwellTracker.Reset(); return; }
        dwellTracker.Complete(DwellClock, CanRepeatDwell(target));
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
            bool changesMouseAction = target == DwellTarget.DragDrop || mouseActions.Selected != MouseAction.Left;
            ReportInputResult(mouseActions.IsWindowDragging ? FinishWindowDrag() : mouseActions.Perform());
            RefreshDragKey();
            if (changesMouseAction) SuppressCurrentDwell();
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
        inputRecoveryPending = true;
        UpdateDwellTimer();
        // Keep each request registered until its continuation has finished.
        emailInsertion?.Cancel();
        passwordVerification?.Cancel();
        desktopDwellTargets?.Leave();
        SuppressCurrentDwell();
        mouseWin?.ResetDwellPreview();
        ReleaseRecoveredInput();
        if (openMenu >= 0) CloseMenu();
        ClearFocusedSuggestions();
        ClearWordBuffer();
        checkingDisplayedWord = false;
        UpdateDwellTimer();
    }

    void ReleaseRecoveredInput()
    {
        bool mouseReleased = (!mouseActions.IsDragging && mouseActions.Selected == MouseAction.Left) || CancelMouseActionCore(false);
        bool keysReleased = held.Count == 0 || ReleaseHeldModifiersCore(false);
        inputRecoveryPending = !mouseReleased || !keysReleased;
        if (inputRecoveryPending && statusMessage != InputRecoveryStatus) ShowStatusMessage(InputRecoveryStatus);
        else if (!inputRecoveryPending && statusMessage == InputRecoveryStatus) ShowStatusMessage(null);
    }
}
